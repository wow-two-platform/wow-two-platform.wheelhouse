using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Application.Deployments.SelfDeploy.Constants;
using Wheelhouse.Application.Deployments.SelfDeploy.Models;
using Wheelhouse.Application.Deployments.SelfDeploy.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.SelfDeploy.QueryHandlers;

/// <summary>Handles <see cref="SelfDeploymentQuery"/>. A self-promotion replaces the console that started it, so the
/// new console reads the outcome back: its own target is the Wheelhouse target of the environment it serves, and the
/// newest rollout there that reached the host is the one shown. One the records still call under way is first read
/// again from the host's journal, which outlives every console.</summary>
public sealed class SelfDeploymentQueryHandler(
    IConsoleIdentity console, DeploymentHistoryService history, IDeploymentGateway gateway)
    : IQueryHandler<SelfDeploymentQuery, AppResult<SelfDeploymentDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<SelfDeploymentDto>> HandleAsync(SelfDeploymentQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<SelfDeploymentDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);
        var target = loaded.Targets.FirstOrDefault(row => row.Product == SelfProduct.Slug && row.Environment == console.Environment);
        if (target is null)
            return AppResult<SelfDeploymentDto>.Ok(new SelfDeploymentDto(console.Environment, null, console.BootedAt, null));

        var latest = Latest(loaded, target.Slug);
        if (latest is { Outcome: DeploymentOutcome.Pending or DeploymentOutcome.Unknown })
        {
            // The host's journal holds the outcome; the read refreshes the local observation the history reads.
            await gateway.ReadAsync("status", latest.Record.Id, cancellationToken);
            if (await history.LoadAsync(cancellationToken) is AppResult<DeploymentHistory>.Success { Data: var again })
                latest = Latest(again, target.Slug);
        }

        return AppResult<SelfDeploymentDto>.Ok(new SelfDeploymentDto(console.Environment, target.Slug, console.BootedAt,
            latest is null ? null : Map(latest, console.BootedAt)));
    }

    /// <summary>The newest rollout that reached the target; a refused or never-applied deploy changed nothing. @internal</summary>
    private static DeploymentEntry? Latest(DeploymentHistory history, string target) =>
        history.Entries.FirstOrDefault(entry => entry.Record.TargetId == target && entry.Record.Intent is null
            && entry.Outcome != DeploymentOutcome.Refused);

    /// <summary>Maps one rollout. @internal</summary>
    private static SelfDeploymentRunDto Map(DeploymentEntry entry, DateTimeOffset bootedAt) =>
        new(entry.Record.Id, entry.Record.Release, entry.Previous?.Release, entry.Record.Status, entry.Outcome,
            entry.Record.Actor, entry.Record.DeployReason, entry.Record.Reason, entry.Record.SubmittedAt,
            entry.Record.CompletedAt, entry.Record.SubmittedAt < bootedAt);
}
