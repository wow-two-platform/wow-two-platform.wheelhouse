namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a control the owner ran on an environment instead of a deploy: its kind and the service it named.</summary>
/// <param name="Kind"><c>stop</c>, <c>start</c>, <c>restart</c>, <c>reset</c>, <c>run</c> or <c>files-migration</c>.</param>
/// <param name="Services">The service it named; empty for the whole environment and for a files migration.</param>
/// <param name="Set">The migration set a reset rolled back alone; null for anything else.</param>
/// <param name="Command">A run's command label (<c>dotnet Identity.Api.dll admin create --slug …</c>), never its
/// arguments; null for anything else.</param>
/// <param name="Step">A files migration's step (<c>plan</c>, <c>push</c> or <c>verify</c>); null for anything else.</param>
/// <param name="Sources">A files migration's source ids; null for anything else.</param>
public sealed record DeploymentOperationDto(
    string Kind, IReadOnlyList<string> Services, string? Set = null, string? Command = null, string? Step = null,
    IReadOnlyList<string>? Sources = null)
{
    /// <summary>Gets the service it named; a record without one reads empty, as a files migration's does.</summary>
    public IReadOnlyList<string> Services { get; init; } = Services ?? [];
}
