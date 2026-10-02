using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults.Commands;
using Wheelhouse.Application.Vaults.Models;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.CommandHandlers;

/// <summary>Handles <see cref="VaultUpdateCommand"/>.</summary>
public sealed class VaultUpdateCommandHandler(IVaultsRepository vaults, IServersRepository servers)
    : ICommandHandler<VaultUpdateCommand, AppResult<VaultDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultDto>> HandleAsync(VaultUpdateCommand request, CancellationToken cancellationToken)
    {
        var vault = await vaults.GetBySlugAsync(request.Slug, cancellationToken);
        if (vault is null)
            return AppResult<VaultDto>.Fail(AppErrorFactory.NotFound($"Vault '{request.Slug}' was not found."));
        var server = await servers.GetBySlugAsync(request.Server, cancellationToken);
        if (server is null)
            return AppResult<VaultDto>.Fail(AppErrorFactory.Validation($"Server '{request.Server}' was not found."));

        vault.Name = request.Name.Trim();
        vault.ServerId = server.Id;
        vault.Url = request.Url.TrimEnd('/');
        await vaults.UpdateAsync(vault, cancellationToken);
        return AppResult<VaultDto>.Ok(new VaultDto { Slug = vault.Slug, Name = vault.Name, Server = server.Slug, Url = vault.Url });
    }
}
