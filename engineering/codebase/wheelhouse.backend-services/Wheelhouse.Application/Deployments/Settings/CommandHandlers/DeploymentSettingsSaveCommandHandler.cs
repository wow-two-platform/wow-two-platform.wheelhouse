using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Domain.Settings.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Sessions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers.Generators;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Writes an environment's values in one transaction: each value is checked against its declared type, a key
/// that reads as a secret is refused as a plain setting unless confirmed, then secrets become new vault versions and
/// settings go to the store; a key moving between the two leaves the other. Any refusal or failure writes nothing.</summary>
/// <remarks>While the standalone-vault fallback is on, secrets go to that vault first and outside the transaction.</remarks>
public sealed class DeploymentSettingsSaveCommandHandler(
    ITargetsRepository targets,
    IProductsRepository products,
    IEnvironmentSettingsRepository store,
    IEnvironmentSecrets secrets,
    IDataSession session,
    DeploymentSettingsService settings,
    IIdGenerator ids)
    : ICommandHandler<DeploymentSettingsSaveCommand, AppResult<DeploySettingsSavedDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploySettingsSavedDto>> HandleAsync(DeploymentSettingsSaveCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        var product = target is null ? null : await products.GetByIdAsync(target.ProductId, cancellationToken);
        if (target is null || product is null)
            return AppResult<DeploySettingsSavedDto>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));

        var declarations = await settings.DeclarationsAsync(target, product, request.Release, cancellationToken);
        var problems = Check(request.Values, declarations);
        if (problems.Count > 0)
            return AppResult<DeploySettingsSavedDto>.Fail(AppErrorFactory.Validation(string.Join(" ", problems)));

        // A generated secret's value is made here and goes to the vault only; the reply names its version alone.
        var values = request.Values.Select(value => value.Generate is { } format ? value with { Value = ids.GenerateSecret(format) } : value).ToList();
        var generated = values.Where(value => value.Generate is not null).Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
        var removals = values.Where(value => value.Remove).Select(value => value.Key).ToList();
        var writes = values.Where(value => value.Secret && !value.Remove)
            .Select(value => new SecretWrite(value.Key, value.Value, value.Note, value.Expiry)).ToList();
        var plain = values.Where(value => !value.Secret && !value.Remove)
            .ToDictionary(value => value.Key, value => Normalize(value, declarations), StringComparer.Ordinal);

        // Completing the unit saves what the vault staged and commits; any return before it keeps nothing.
        await using var unit = await session.BeginAsync(cancellationToken);
        var written = await secrets.WriteAsync(target, product, writes, request.Actor, cancellationToken);
        if (written is AppResult<IReadOnlyList<SecretWritten>>.Failure refused)
            return AppResult<DeploySettingsSavedDto>.Fail(refused.Error);
        // A key lives in one layer: a secret leaves the store, a plain setting leaves the vault, a removal leaves both.
        var cleared = await secrets.RemoveAsync(target, product, [.. plain.Keys.Concat(removals)], cancellationToken);
        if (cleared is AppResult<IReadOnlyDictionary<string, int>>.Failure failed)
            return AppResult<DeploySettingsSavedDto>.Fail(failed.Error);
        var removedSecrets = ((AppResult<IReadOnlyDictionary<string, int>>.Success)cleared).Data;
        var removedSettings = await store.RemoveAsync(product.Id, target.Environment, [.. writes.Select(write => write.Key).Concat(removals)], cancellationToken);
        if (plain.Count > 0)
            await store.SetAsync(product.Id, target.Environment, plain, cancellationToken);
        await unit.CompleteAsync(cancellationToken);

        var versions = ((AppResult<IReadOnlyList<SecretWritten>>.Success)written).Data;
        var keys = new List<SavedSettingDto>();
        keys.AddRange(versions.Select(item => new SavedSettingDto(item.Key, true, false, item.Version is > 0 ? item.Version : null,
            string.IsNullOrEmpty(item.Fingerprint) ? null : item.Fingerprint, generated.Contains(item.Key))));
        keys.AddRange(plain.Keys.Select(key => new SavedSettingDto(key, false, false)));
        keys.AddRange(removals.Where(key => removedSecrets.ContainsKey(key) || removedSettings.Contains(key))
            .Select(key => new SavedSettingDto(key, removedSecrets.ContainsKey(key), true, removedSecrets.TryGetValue(key, out var version) ? version : null)));
        request.Saved.Keys = [.. keys.OrderBy(key => key.Key, StringComparer.Ordinal)];
        return AppResult<DeploySettingsSavedDto>.Ok(new DeploySettingsSavedDto(plain.Count, versions.Count) { Keys = request.Saved.Keys });
    }

    /// <summary>Why the values cannot be saved: a value that does not fit its declared type, a declared or secret-looking
    /// key given as a plain setting without the owner's confirmation, a generated value for a key typed other than
    /// text. Never quotes a value.</summary>
    /// <param name="values">The values.</param>
    /// <param name="declarations">What the release and the owner declare about each key.</param>
    /// <returns>The problems, one sentence each.</returns>
    public static IReadOnlyList<string> Check(IReadOnlyList<DeploySettingValue> values, IReadOnlyDictionary<string, KeyDeclaration> declarations)
    {
        var problems = new List<string>();
        foreach (var value in values.Where(value => !value.Remove))
        {
            var declared = declarations.GetValueOrDefault(value.Key);
            if (!value.Secret && !value.ConfirmPlain
                && (declared?.Secret == true || (declared?.Plain != true && SettingTypes.LooksSecret(value.Key))))
                problems.Add(value.Key + (declared?.Secret == true ? " is declared a secret" : " reads as a secret")
                    + "; save it as a secret, or confirm it is a plain setting.");
            if (declared is null)
                continue;
            if (value.Generate is not null)
            {
                if (declared.Type != SettingType.Text)
                    problems.Add(value.Key + " takes a " + SettingTypes.NameOf(declared.Type) + "; only a text secret's value is generated.");
                continue;
            }
            var candidate = declared.Type == SettingType.Boolean ? value.Value.Trim().ToLowerInvariant() : value.Value;
            if (SettingTypes.Check(declared.Type, candidate, declared.Format, declared.Choices) is { } problem)
                problems.Add(value.Key + " " + problem + ".");
        }
        return problems;
    }

    /// <summary>A plain value as stored: a boolean in lowercase, anything else as given. @internal</summary>
    private static string Normalize(DeploySettingValue value, IReadOnlyDictionary<string, KeyDeclaration> declarations) =>
        declarations.GetValueOrDefault(value.Key)?.Type == SettingType.Boolean ? value.Value.Trim().ToLowerInvariant() : value.Value;
}
