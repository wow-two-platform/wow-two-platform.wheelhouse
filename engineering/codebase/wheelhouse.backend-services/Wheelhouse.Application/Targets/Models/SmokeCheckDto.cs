namespace Wheelhouse.Application.Targets.Models;

/// <summary>Represents a request the runner makes to a service after a rollout.</summary>
public sealed record SmokeCheckDto
{
    /// <summary>Gets the service's name.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the path requested.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the HTTP status the service must answer.</summary>
    public required int Status { get; init; }
}
