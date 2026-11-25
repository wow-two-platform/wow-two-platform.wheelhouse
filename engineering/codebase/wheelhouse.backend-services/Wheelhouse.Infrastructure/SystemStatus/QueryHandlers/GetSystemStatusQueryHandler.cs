using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SystemStatus.Models;
using Wheelhouse.Application.SystemStatus.Queries;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.SystemStatus.QueryHandlers;

/// <summary>Reads the environment and rehearsal mode of this console.</summary>
public sealed class GetSystemStatusQueryHandler(DeploymentSettings deployment, IConsoleIdentity console)
    : IQueryHandler<GetSystemStatusQuery, AppResult<SystemStatusModel>>
{
    /// <inheritdoc />
    public ValueTask<AppResult<SystemStatusModel>> HandleAsync(GetSystemStatusQuery query, CancellationToken cancellationToken)
    {
        var status = new SystemStatusModel("Wheelhouse", "ok", query.Version, console.Environment, deployment.IsLocalRig);
        return ValueTask.FromResult(AppResult<SystemStatusModel>.Ok(status));
    }
}
