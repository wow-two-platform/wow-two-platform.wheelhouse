namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents what a files migration job kept: the import it worked on and, per source, how many files and
/// bytes the mover counted. A source appears once the mover reported its result; never a token or a value.</summary>
/// <param name="Import">The import id a push opened or a verify checks; null for a plan.</param>
/// <param name="Results">Each finished source's result, by source id.</param>
public sealed record FilesMigrationDto(string? Import, IReadOnlyDictionary<string, FilesMigrationResultDto> Results)
{
    /// <summary>Gets the finished sources' results; a record without any reads empty.</summary>
    public IReadOnlyDictionary<string, FilesMigrationResultDto> Results { get; init; } = Results ?? new Dictionary<string, FilesMigrationResultDto>();
}

/// <summary>Represents one source's result as the mover counted it.</summary>
/// <param name="Files">The files counted, pushed or compared.</param>
/// <param name="Bytes">Their bytes.</param>
/// <param name="Skipped">The files left out because the import already holds them.</param>
/// <param name="Failed">The files that did not move.</param>
public sealed record FilesMigrationResultDto(long Files, long Bytes, long Skipped = 0, long Failed = 0);
