using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Commands;

/// <summary>Plans, pushes or verifies a product's legacy files into a Files host for one dev target. The sources come
/// from the product's <c>files-migration.yml</c> at the commit the target runs, picked by wave and/or id. A push without
/// <paramref name="Import"/> opens an import on the Files host; the token Files answers reaches the runner's standard
/// input only, never the audit, the job or a log.</summary>
/// <param name="Target">The target whose legacy files move.</param>
/// <param name="Step"><c>plan</c>, <c>push</c> or <c>verify</c>.</param>
/// <param name="Wave">The wave to take; null for any.</param>
/// <param name="Sources">The source ids to take; null or empty for any.</param>
/// <param name="FilesTarget">The Files target (product <c>files</c>, same environment) the files move into.</param>
/// <param name="FilesUrl">The Files host's https URL, ending with <c>/</c>.</param>
/// <param name="Import">An import to continue or verify; null opens one on a push.</param>
/// <param name="Actor">Who does it.</param>
public sealed record FilesMigrationCommand(
    string Target, string Step, int? Wave, IReadOnlyList<string>? Sources, string FilesTarget, string FilesUrl, string? Import, string Actor)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <summary>Holds the steps.</summary>
    public static readonly IReadOnlyList<string> Steps = ["plan", "push", "verify"];

    /// <summary>Gets or sets the sources the handler picked, set once they are known; the audit names them.</summary>
    public IReadOnlyList<string>? Picked { get; set; }

    /// <summary>Gets or sets the import id the handler opened, set once Files answers; the audit names it.</summary>
    public string? Opened { get; set; }

    /// <inheritdoc />
    public string AuditAction => "files-migration." + Step;

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string? AuditDetail => string.Join("; ", new[]
    {
        "sources: " + string.Join(",", Picked ?? Sources ?? []),
        Wave is null ? null : "wave: " + Wave,
        (Opened ?? Import) is { } import ? "import: " + import : null,
        "files: " + FilesTarget,
    }.OfType<string>());
}
