using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults.Commands;
using Wheelhouse.Application.Vaults.Models;
using Wheelhouse.Domain.Vaults.Entities;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.CommandHandlers;

/// <summary>Handles <see cref="VaultCreateCommand"/>; the vault opens nothing until its administrator password is on
/// the control host.</summary>
public sealed class VaultCreateCommandHandler(IVaultsRepository vaults, IServersRepository servers)
    : ICommandHandler<VaultCreateCommand, AppResult<VaultDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultDto>> HandleAsync(VaultCreateCommand request, CancellationToken cancellationToken)
    {
        if (await vaults.GetBySlugAsync(request.Slug, cancellationToken) is not null)
            return AppResult<VaultDto>.Fail(AppErrorFactory.Conflict($"A vault is already named '{request.Slug}'."));
        var server = await servers.GetBySlugAsync(request.Server, cancellationToken);
        if (server is null)
            return AppResult<VaultDto>.Fail(AppErrorFactory.Validation($"Server '{request.Server}' was not found."));

        var vault = await vaults.CreateAsync(new VaultEntity
        {
            Id = Guid.CreateVersion7(),
            Slug = request.Slug,
            Name = request.Name.Trim(),
            ServerId = server.Id,
            Url = request.Url.TrimEnd('/'),
        }, cancellationToken);
        return AppResult<VaultDto>.Ok(new VaultDto { Slug = vault.Slug, Name = vault.Name, Server = server.Slug, Url = vault.Url });
    }
}
