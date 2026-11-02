using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Controls.Commands;

/// <summary>Reads a run's last line as the command printed it, once (v0.13): the line the job keeps scrubbed, such as an
/// invitation link, which the target kept for one read and removes with it. The audit names the run, never the line.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Job">The run's deployment id.</param>
/// <param name="Actor">Who reads it.</param>
public sealed record RunRevealCommand(string Target, Guid Job, string Actor) : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "service.run.reveal";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => "deployment " + Job.ToString("D");
}
