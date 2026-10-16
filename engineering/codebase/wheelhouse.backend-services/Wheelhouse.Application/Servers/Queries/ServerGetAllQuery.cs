using Wheelhouse.Application.Servers.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.Queries;

/// <summary>Represents a query to get every server.</summary>
public sealed record ServerGetAllQuery : IQuery<AppResult<IReadOnlyList<ServerDto>>>;
