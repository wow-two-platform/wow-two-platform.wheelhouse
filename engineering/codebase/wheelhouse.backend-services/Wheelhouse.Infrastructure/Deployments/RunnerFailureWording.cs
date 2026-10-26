using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Words a failed runner call for the owner by its <see cref="ProcessFailureKind"/>: the runner's own reason when its
/// failure line gave one, else a fixed message. Never repeats arguments, output or the process client's metadata.</summary>
/// <param name="Rejected">The prefix of a refused precondition's reason.</param>
/// <param name="StepFailed">The prefix of a failed step's reason.</param>
/// <param name="Failed">The message of a failure without a reason.</param>
/// <param name="TimedOut">The message of a call past its time limit.</param>
/// <param name="Unavailable">The message of a runner that could not start or answered no JSON.</param>
public sealed record RunnerFailureWording(string Rejected, string StepFailed, string Failed, string TimedOut, string Unavailable)
{
    /// <summary>Holds the deployment runner's (<c>transport.py</c>) wording.</summary>
    public static RunnerFailureWording Deployment { get; } = new(
        "Deployment rejected: ",
        "Deployment step failed: ",
        "Deployment operation failed. Inspect the target privately.",
        "Deployment response timed out. Reconcile target state before retrying.",
        "Deployment runner is unavailable.");

    /// <summary>Holds the infrastructure runner's (<c>infra.py</c>) wording: the reason alone.</summary>
    public static RunnerFailureWording Infra { get; } = new(
        "",
        "",
        "The infrastructure action failed. Inspect the control host privately.",
        "The infrastructure action timed out.",
        "The infrastructure runner is unavailable.");

    /// <summary>Words a failure the runner's process client returned.</summary>
    /// <param name="error">The client's error.</param>
    /// <returns>The error the owner reads: a conflict for a refusal, unavailable for a failed step or a runner that is
    /// down, a timeout (both transient, so MCP marks them retryable), else unexpected.</returns>
    public AppError Describe(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return ProcessFailureMapper.KindOf(error) switch
        {
            // The parser read the runner's reason: a refusal is a conflict; any other failure kind is its default type.
            ProcessFailureKind.Exited when error.Type == AppErrorType.Conflict => AppErrorFactory.Conflict(Rejected + error.Message),
            ProcessFailureKind.Exited when error.Type == AppErrorType.ExternalUnavailable =>
                AppErrorFactory.ExternalUnavailable(StepFailed + error.Message),
            ProcessFailureKind.Exited => AppErrorFactory.Unexpected(Failed),
            ProcessFailureKind.TimedOut => AppErrorFactory.OperationTimeout(TimedOut),
            _ => AppErrorFactory.ExternalUnavailable(Unavailable),
        };
    }
}
