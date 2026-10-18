namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents the watch window after a successful rollout: its containers and sites looked at once a minute.</summary>
public sealed record DeploymentWatchDto
{
    /// <summary>Gets <c>watching</c>, <c>passed</c>, <c>degraded</c>, <c>superseded</c> or <c>unverified</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets when the window opened: the rollout's completion.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets when the window closes.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>Gets how many looks read the target.</summary>
    public int? Checks { get; init; }

    /// <summary>Gets what regressed: a container that stopped, turned unhealthy or restarted, or a site gone silent.</summary>
    public IReadOnlyList<string> Findings { get; init; } = [];

    /// <summary>Gets when a look first found a regression.</summary>
    public DateTimeOffset? DegradedAt { get; init; }

    /// <summary>Gets when the window ended.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}
