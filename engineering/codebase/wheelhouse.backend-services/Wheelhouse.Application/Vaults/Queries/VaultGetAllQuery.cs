using Wheelhouse.Application.Vaults.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Vaults.Queries;

/// <summary>Represents a query to get every vault's definition.</summary>
public sealed record VaultGetAllQuery : IQuery<AppResult<IReadOnlyList<VaultDto>>>;
