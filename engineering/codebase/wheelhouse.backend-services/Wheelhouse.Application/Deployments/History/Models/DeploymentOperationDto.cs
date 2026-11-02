namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a control the owner ran on an environment instead of a deploy: its kind and the service it named.</summary>
/// <param name="Kind"><c>stop</c>, <c>start</c>, <c>restart</c>, <c>reset</c> or <c>run</c>.</param>
/// <param name="Services">The service it named; empty for the whole environment.</param>
/// <param name="Set">The migration set a reset rolled back alone; null for anything else.</param>
/// <param name="Command">A run's command label (<c>dotnet Identity.Api.dll admin create --slug …</c>), never its
/// arguments; null for anything else.</param>
public sealed record DeploymentOperationDto(string Kind, IReadOnlyList<string> Services, string? Set = null, string? Command = null);
