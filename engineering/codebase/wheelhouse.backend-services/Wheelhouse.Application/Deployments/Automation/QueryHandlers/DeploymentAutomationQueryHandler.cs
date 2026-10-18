using Wheelhouse.Application.Deployments.Automation.Models;
using Wheelhouse.Application.Deployments.Automation.Queries;
using Wheelhouse.Application.Deployments.Automation.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Automation.QueryHandlers;

/// <summary>Handles <see cref="DeploymentAutomationQuery"/>.</summary>
public sealed class DeploymentAutomationQueryHandler(DeploymentAutomationService automation)
    : IQueryHandler<DeploymentAutomationQuery, AppResult<DeploymentAutomationDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploymentAutomationDto>> HandleAsync(
        DeploymentAutomationQuery request, CancellationToken cancellationToken) =>
        AppResult<DeploymentAutomationDto>.Ok(await automation.DescribeAsync(cancellationToken));
}
