using System.Security.Cryptography;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Constants;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.QueryHandlers;

/// <summary>Handles <see cref="VaultIssuesQuery"/>: each lapsing secret names its product, environment and target. A
/// sealed vault is an issue only once it holds secrets, so a fresh dev console without a key stays quiet; the root key's
/// checks raise one only once something is encrypted under it, and a failed file check only when the sealed state does
/// not already name its cause.</summary>
public sealed class VaultIssuesQueryHandler(
    SecretsVaultService vault, IProductsRepository products, ITargetsRepository targets, ISecretsVaultStore store,
    RootKeyChecks rootKey, RootKeyFileMonitor fileChecks)
    : IQueryHandler<VaultIssuesQuery, AppResult<VaultIssuesDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultIssuesDto>> HandleAsync(VaultIssuesQuery request, CancellationToken cancellationToken)
    {
        var (root, reason) = await vault.UnsealAsync(cancellationToken);
        if (root is not null)
            CryptographicOperations.ZeroMemory(root);
        var held = (await store.GetAllSecretsAsync(cancellationToken)).Any(secret => secret.RemovedAt is null);
        var slugs = (await products.GetAllAsync(cancellationToken)).ToDictionary(product => product.Id, product => product.Slug);
        var places = (await targets.GetAllAsync(cancellationToken))
            .GroupBy(target => (target.ProductId, target.Environment))
            .ToDictionary(group => group.Key, group => group.First().Slug);
        var expiries = (await vault.ExpiriesAsync(cancellationToken))
            .Where(item => slugs.ContainsKey(item.Secret.ProductId))
            .Select(item => new VaultExpiryDto(slugs[item.Secret.ProductId], item.Secret.Environment.ToString().ToLowerInvariant(),
                places.GetValueOrDefault((item.Secret.ProductId, item.Secret.Environment)), item.Secret.Key, item.Secret.ExpiresAt!.Value,
                item.Level))
            .ToList();
        var sealedShown = held ? reason : null;
        var offlineCopy = await rootKey.OfflineCopyAsync(cancellationToken);
        var fileCheck = fileChecks.Last is { Problem: { } problem } last && await rootKey.IsBoundAsync(cancellationToken)
            && !(sealedShown is not null && problem is SecretsVaultStates.Missing or SecretsVaultStates.Invalid or SecretsVaultStates.Mismatch)
            ? last
            : null;
        return AppResult<VaultIssuesDto>.Ok(new VaultIssuesDto(
            reason is null ? SecretsVaultStates.Unsealed : SecretsVaultStates.Sealed, sealedShown, expiries,
            offlineCopy.Due ? offlineCopy : null, fileCheck));
    }
}
