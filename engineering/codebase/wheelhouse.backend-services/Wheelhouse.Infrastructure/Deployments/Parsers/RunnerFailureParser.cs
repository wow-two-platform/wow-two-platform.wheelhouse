using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Infrastructure.Deployments.Parsers;

/// <summary>Parses the runner's JSON failure line into an operator-safe deployment error.</summary>
/// <remarks>
/// The runner writes one JSON object to standard error. Only its <c>reason</c> carries operator-safe text:
/// static messages and contract key names, never setting values or command output.
/// </remarks>
public sealed class RunnerFailureParser
{
    private const int MaxReasonLength = 300;

    /// <summary>Parses the runner's standard error into the error returned to the operator.</summary>
    /// <param name="standardError">The runner's standard error; only its last non-empty line is read.</param>
    /// <returns>
    /// A conflict when the runner refused a precondition, an unavailable error when a step failed, or
    /// <see langword="null"/> when the output is empty, malformed or carries no reason.
    /// </returns>
    public AppError? Parse(string standardError)
    {
        var line = standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (line is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
                return null;

            var text = new string(reason.GetString()!.Where(c => !char.IsControl(c)).Take(MaxReasonLength).ToArray()).Trim();
            if (text.Length == 0)
                return null;

            var refused = root.TryGetProperty("failure", out var failure)
                && failure.ValueKind == JsonValueKind.String && failure.GetString() == "Rejected";
            return refused
                ? AppErrorFactory.Conflict("Deployment rejected: " + text)
                : AppErrorFactory.ExternalUnavailable("Deployment step failed: " + text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
