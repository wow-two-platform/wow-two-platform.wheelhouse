using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Vaults.Hygiene;

/// <summary>Reads every namespace's secret and token metadata, then applies <see cref="VaultHygieneRules"/>.</summary>
public sealed class VaultHygieneQueryHandler(IVaultGateway gateway, TimeProvider time)
    : IQueryHandler<VaultHygieneQuery, AppResult<VaultHygiene>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultHygiene>> HandleAsync(VaultHygieneQuery request, CancellationToken cancellationToken)
    {
        var listed = await gateway.ReadAsync(VaultResource.Namespaces, request.Vault, null, cancellationToken);
        if (listed is not AppResult<JsonElement>.Success { Data: var namespaces })
            return AppResult<VaultHygiene>.Fail(((AppResult<JsonElement>.Failure)listed).Error);
        try
        {
            var inventory = new List<NamespaceInventory>();
            // Sequential reads share one vault session and keep the vault's own throttle out of play.
            foreach (var slug in namespaces.EnumerateArray().Select(ns => ns.GetProperty("slug").GetString() ?? ""))
            {
                var secrets = await gateway.ReadAsync(VaultResource.Secrets, request.Vault, slug, cancellationToken);
                if (secrets is not AppResult<JsonElement>.Success { Data: var secretList })
                    return AppResult<VaultHygiene>.Fail(((AppResult<JsonElement>.Failure)secrets).Error);
                var tokens = await gateway.ReadAsync(VaultResource.Tokens, request.Vault, slug, cancellationToken);
                if (tokens is not AppResult<JsonElement>.Success { Data: var tokenList })
                    return AppResult<VaultHygiene>.Fail(((AppResult<JsonElement>.Failure)tokens).Error);
                inventory.Add(new NamespaceInventory(slug,
                    secretList.Deserialize<List<SecretFacts>>(JsonSerializerOptions.Web) ?? [],
                    tokenList.Deserialize<List<TokenFacts>>(JsonSerializerOptions.Web) ?? []));
            }
            return AppResult<VaultHygiene>.Ok(VaultHygieneRules.Evaluate(request.Vault, inventory, time.GetUtcNow()));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return AppResult<VaultHygiene>.Fail(AppErrorFactory.ExternalUnavailable("The vault returned unreadable metadata."));
        }
    }
}
