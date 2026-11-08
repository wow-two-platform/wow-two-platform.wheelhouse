using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Plans, pushes or verifies a product's legacy files into a Files host for one dev target.</summary>
/// <param name="Step"><c>plan</c> counts files and bytes, <c>push</c> uploads them, <c>verify</c> compares them.</param>
/// <param name="Wave">The manifest wave to take; null for any.</param>
/// <param name="Sources">The source ids to take; null for any. Waves and ids narrow together.</param>
/// <param name="FilesTarget">The Files target of the same environment the files move into.</param>
/// <param name="FilesUrl">The Files host's https URL, ending with <c>/</c>, on that target's app site host or an alias.</param>
/// <param name="Import">An import to continue or verify; a push without one opens an import.</param>
public sealed record FilesMigrationRequest(
    [Required] string? Step, int? Wave, IReadOnlyList<string>? Sources, [Required] string? FilesTarget, [Required] string? FilesUrl,
    string? Import = null);
