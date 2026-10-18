using Wheelhouse.Application.Deployments.Automation.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Automation.Queries;

/// <summary>Represents a query for every product's automation choices and the automatic deploys waiting.</summary>
public sealed record DeploymentAutomationQuery : IQuery<AppResult<DeploymentAutomationDto>>;
