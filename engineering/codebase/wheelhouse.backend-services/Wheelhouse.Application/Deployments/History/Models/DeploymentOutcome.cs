namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Holds what a deployment status amounts to; the wire carries these lowercase names.</summary>
public static class DeploymentOutcome
{
    /// <summary>The rollout succeeded, with or without warnings.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The rollout failed, rolled back, failed to roll back or was interrupted.</summary>
    public const string Failed = "failed";

    /// <summary>The runner refused the submission before changing anything.</summary>
    public const string Refused = "refused";

    /// <summary>The rollout is submitting, queued or running.</summary>
    public const string Pending = "pending";

    /// <summary>The control plane holds no receipt from the target.</summary>
    public const string Unknown = "unknown";

    private static readonly HashSet<string> FailedStatuses = ["failed", "rolled_back", "rollback_failed", "interrupted"];
    private static readonly HashSet<string> PendingStatuses = ["submitting", "queued", "running"];

    /// <summary>Maps a runner status to its outcome.</summary>
    /// <param name="status">The runner status, such as <c>rolled_back</c>.</param>
    /// <returns>One of this class's outcomes.</returns>
    public static string Of(string status) =>
        status == "succeeded" ? Succeeded
        : FailedStatuses.Contains(status) ? Failed
        : status == "rejected" ? Refused
        : PendingStatuses.Contains(status) ? Pending
        : Unknown;
}
