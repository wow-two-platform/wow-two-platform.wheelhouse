namespace Wheelhouse.Domain.Targets.Models;

/// <summary>Represents a request the runner makes to a service after a rollout, and the status it must answer.</summary>
public sealed record SmokeCheckValueObject
{
    /// <summary>Gets the service's name in the product's Compose definition.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the path requested, starting with <c>/</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the HTTP status the service must answer.</summary>
    public int Status { get; init; } = 200;
}
