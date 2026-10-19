using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Vaults.Changes;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Writes an environment's values: secrets first, into its vault namespace (created when missing), then the
/// settings into the store, so a vault failure leaves the store as it was.</summary>
public sealed class DeploymentSettingsSaveCommandHandler(
    ITargetsRepository targets,
    IProductsRepository products,
    IEnvironmentSettingsRepository store,
    IVaultGateway vaults,
    DeploymentSettingsService settings)
    : ICommandHandler<DeploymentSettingsSaveCommand, AppResult<DeploySettingsSavedDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploySettingsSavedDto>> HandleAsync(DeploymentSettingsSaveCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        var product = target is null ? null : await products.GetByIdAsync(target.ProductId, cancellationToken);
        if (target is null || product is null)
            return AppResult<DeploySettingsSavedDto>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));

        var secrets = request.Values.Where(value => value.Secret).ToList();
        if (secrets.Count > 0)
        {
            if (await settings.VaultOfAsync(target, product, cancellationToken) is not { } place)
                return AppResult<DeploySettingsSavedDto>.Fail(AppErrorFactory.Conflict(
                    "No vault holds this environment's secrets yet; add a vault under Servers first."));
            var created = false;
            foreach (var secret in secrets)
            {
                var change = new SecretSetChange { Namespace = place.Namespace, Key = secret.Key, Value = secret.Value };
                var written = await vaults.ApplyAsync(place.Vault, change, cancellationToken);
                if (written is AppResult<JsonElement>.Failure { Error.Type: AppErrorType.NotFound } && !created)
                {
                    // The environment's first secret: its namespace comes with it.
                    created = true;
                    var space = await vaults.ApplyAsync(place.Vault,
                        new NamespaceCreateChange { Namespace = place.Namespace, Name = product.Name + " " + DeploymentSettingsService.EnvironmentName(target.Environment) },
                        cancellationToken);
                    if (space is AppResult<JsonElement>.Failure { Error.Type: not AppErrorType.Conflict } refused)
                        return AppResult<DeploySettingsSavedDto>.Fail(refused.Error);
                    written = await vaults.ApplyAsync(place.Vault, change, cancellationToken);
                }
                if (written is AppResult<JsonElement>.Failure failure)
                    return AppResult<DeploySettingsSavedDto>.Fail(failure.Error);
            }
        }

        var plain = request.Values.Where(value => !value.Secret).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        if (plain.Count > 0)
            await store.SetAsync(product.Id, target.Environment, plain, cancellationToken);
        return AppResult<DeploySettingsSavedDto>.Ok(new DeploySettingsSavedDto(plain.Count, secrets.Count));
    }
}
