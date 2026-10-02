using Wheelhouse.Application.Vaults.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.Queries;

/// <summary>Represents a query to get every vault's definition.</summary>
public sealed record VaultGetAllQuery : IQuery<AppResult<IReadOnlyList<VaultDto>>>;
