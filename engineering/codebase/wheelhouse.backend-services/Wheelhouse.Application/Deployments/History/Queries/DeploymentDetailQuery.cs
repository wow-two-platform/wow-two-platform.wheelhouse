using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Reads one deployment in full, from local records only.</summary>
/// <param name="Id">The submission's identifier.</param>
public sealed record DeploymentDetailQuery(Guid Id) : IQuery<AppResult<DeploymentDetailDto>>;
