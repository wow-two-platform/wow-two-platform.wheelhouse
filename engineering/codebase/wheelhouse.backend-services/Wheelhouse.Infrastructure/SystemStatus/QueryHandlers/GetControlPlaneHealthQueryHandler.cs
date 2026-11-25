using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wheelhouse.Application.SystemStatus.Constants;
using Wheelhouse.Application.SystemStatus.Models;
using Wheelhouse.Application.SystemStatus.Queries;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.SystemStatus.QueryHandlers;

/// <inheritdoc />
public sealed class GetControlPlaneHealthQueryHandler(HealthCheckService health, DeploymentSettings deployment)
    : IQueryHandler<GetControlPlaneHealthQuery, AppResult<ControlPlaneHealth>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ControlPlaneHealth>> HandleAsync(GetControlPlaneHealthQuery query, CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(check => check.Name == ControlPlaneHealthCheckNames.Database, cancellationToken);
        var ready = report.Entries.TryGetValue(ControlPlaneHealthCheckNames.Database, out var database)
            && database.Status == HealthStatus.Healthy;

        return AppResult<ControlPlaneHealth>.Ok(new ControlPlaneHealth("Wheelhouse", ready, query.Version, deployment.IsLocalRig));
    }
}
