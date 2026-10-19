using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="VaultVerifyCommand"/>; the audit entry carries the counts.</summary>
public sealed class VaultVerifyCommandHandler(SecretsVaultService vault, IProductsRepository products)
    : ICommandHandler<VaultVerifyCommand, AppResult<VaultVerificationDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultVerificationDto>> HandleAsync(VaultVerifyCommand request, CancellationToken cancellationToken)
    {
        var slugs = (await products.GetAllAsync(cancellationToken)).ToDictionary(product => product.Id, product => product.Slug);
        var result = await vault.VerifyAsync(slugs, cancellationToken);
        request.Outcome.Detail = Describe(result);
        return AppResult<VaultVerificationDto>.Ok(result);
    }

    /// <summary>The verification in one line of counts.</summary>
    /// <param name="result">The verification.</param>
    /// <returns>The line.</returns>
    public static string Describe(VaultVerificationDto result) =>
        result.State + ", canary " + (result.Canary ? "verified" : "not verified") + ", " + result.Products + " product keys, "
        + result.Secrets + " secrets, " + result.Versions + " versions decrypted, " + result.Failed + " failed";
}
