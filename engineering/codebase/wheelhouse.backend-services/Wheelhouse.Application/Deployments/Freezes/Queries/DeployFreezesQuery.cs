using Wheelhouse.Application.Deployments.Freezes.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Freezes.Queries;

/// <summary>Represents a query for every hold on deploys.</summary>
public sealed record DeployFreezesQuery : IQuery<AppResult<IReadOnlyList<DeployFreezeDto>>>;
