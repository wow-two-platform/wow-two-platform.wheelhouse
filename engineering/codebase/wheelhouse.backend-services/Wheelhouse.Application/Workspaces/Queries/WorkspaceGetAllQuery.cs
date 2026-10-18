using Wheelhouse.Application.Workspaces.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.Queries;

/// <summary>Represents a query to get every workspace, in the owner's order.</summary>
public sealed record WorkspaceGetAllQuery : IQuery<AppResult<IReadOnlyList<WorkspaceDto>>>;
