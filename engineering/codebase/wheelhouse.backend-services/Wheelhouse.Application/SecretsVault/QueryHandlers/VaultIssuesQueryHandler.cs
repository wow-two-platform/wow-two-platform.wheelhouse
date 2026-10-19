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
/// sealed vault is an issue only once it holds secrets, so a fresh dev console without a key stays quiet.</summary>
public sealed class VaultIssuesQueryHandler(
    SecretsVaultService vault, IProductsRepository products, ITargetsRepository targets, ISecretsVaultStore store)
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
        return AppResult<VaultIssuesDto>.Ok(new VaultIssuesDto(
            reason is null ? SecretsVaultStates.Unsealed : SecretsVaultStates.Sealed, held ? reason : null, expiries));
    }
}
