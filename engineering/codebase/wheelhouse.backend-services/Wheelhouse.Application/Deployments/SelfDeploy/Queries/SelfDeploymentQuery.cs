using Wheelhouse.Application.Deployments.SelfDeploy.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.SelfDeploy.Queries;

/// <summary>Reads this console's own newest rollout; one still under way is read again from the host's journal first.</summary>
public sealed record SelfDeploymentQuery : IQuery<AppResult<SelfDeploymentDto>>;
