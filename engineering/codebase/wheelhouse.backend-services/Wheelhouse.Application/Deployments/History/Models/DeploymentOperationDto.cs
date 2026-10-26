namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a control the owner ran on an environment instead of a deploy: its kind and the service it named.</summary>
/// <param name="Kind"><c>stop</c>, <c>start</c> or <c>restart</c>.</param>
/// <param name="Services">The service it named; empty for the whole environment.</param>
public sealed record DeploymentOperationDto(string Kind, IReadOnlyList<string> Services);
