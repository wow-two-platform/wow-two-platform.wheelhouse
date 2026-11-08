using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Commands;

/// <summary>Cancels a running files migration job of a target.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Job">The job's id.</param>
/// <param name="Actor">Who cancels it.</param>
public sealed record FilesMigrationCancelCommand(string Target, Guid Job, string Actor) : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "files-migration.cancel";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string? AuditDetail => "job " + Job.ToString("D");
}
