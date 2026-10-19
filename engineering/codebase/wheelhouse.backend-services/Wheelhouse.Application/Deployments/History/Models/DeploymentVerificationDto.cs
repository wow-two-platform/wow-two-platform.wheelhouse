namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a release's end-to-end check after its dev rollout: the command its descriptor declares, run in a
/// one-off container from one service's release image. Auto-promote takes only a release whose check passed.</summary>
public sealed record DeploymentVerificationDto
{
    /// <summary>Holds the status of a check that passed.</summary>
    public const string Passed = "passed";

    /// <summary>Holds the status of a check that failed, timed out or could not run.</summary>
    public const string Failed = "failed";

    /// <summary>Gets <c>passed</c> or <c>failed</c>; a record that names neither reads as failed.</summary>
    public string Status { get; init; } = Failed;

    /// <summary>Gets the service whose release image ran the check.</summary>
    public string? Service { get; init; }

    /// <summary>Gets when the check started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets when the check ended.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets how many seconds it ran.</summary>
    public int? Seconds { get; init; }

    /// <summary>Gets the check's exit code; null when it ran past its timeout, -1 when it could not run.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Gets the operator-safe outcome, such as <c>Passed</c>, <c>Exited 3</c> or <c>Timed out after 300s</c>.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the last line the check printed, cut at 300 characters.</summary>
    public string? Summary { get; init; }

    /// <summary>Whether a dev rollout's check lets auto-promote take its release: it passed, or the release declares
    /// none. A release that declares a check with no result yet — a rollout that has not run it — waits.</summary>
    /// <param name="verification">The rollout's check; null when it ran none.</param>
    /// <param name="verifies">Whether the release declares a check; null when its bundle is unknown.</param>
    /// <returns>True when nothing holds the release back.</returns>
    public static bool Clears(DeploymentVerificationDto? verification, bool? verifies) =>
        verification is { } check ? check.Status == Passed : verifies != true;
}
