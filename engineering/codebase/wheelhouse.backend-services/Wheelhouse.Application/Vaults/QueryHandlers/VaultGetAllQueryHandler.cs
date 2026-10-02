using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults.Models;
using Wheelhouse.Application.Vaults.Queries;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.QueryHandlers;

/// <summary>Handles <see cref="VaultGetAllQuery"/>.</summary>
public sealed class VaultGetAllQueryHandler(IVaultsRepository vaults, IServersRepository servers)
    : IQueryHandler<VaultGetAllQuery, AppResult<IReadOnlyList<VaultDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<VaultDto>>> HandleAsync(VaultGetAllQuery request, CancellationToken cancellationToken)
    {
        var serverSlugs = (await servers.GetAllAsync(cancellationToken)).ToDictionary(server => server.Id, server => server.Slug);
        IReadOnlyList<VaultDto> listed =
        [
            .. (await vaults.GetAllAsync(cancellationToken)).Select(vault =>
                new VaultDto { Slug = vault.Slug, Name = vault.Name, Server = serverSlugs[vault.ServerId], Url = vault.Url }),
        ];
        return AppResult<IReadOnlyList<VaultDto>>.Ok(listed);
    }
}
