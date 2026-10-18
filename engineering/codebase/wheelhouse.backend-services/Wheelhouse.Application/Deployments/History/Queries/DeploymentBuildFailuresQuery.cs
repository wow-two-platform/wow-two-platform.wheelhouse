using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Lists the products whose newest finished main build failed.</summary>
/// <param name="Filter">Which products count; only its workspaces and products apply.</param>
public sealed record DeploymentBuildFailuresQuery(DeploymentFilter Filter) : IQuery<AppResult<IReadOnlyList<BuildFailureDto>>>;
