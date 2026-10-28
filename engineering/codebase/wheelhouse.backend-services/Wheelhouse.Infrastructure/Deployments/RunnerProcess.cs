using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Holds how the API runs the Python runner — <c>transport.py</c> and <c>infra.py</c> — on the SDK process client: an
/// argument list ending in <c>--root</c>, 110 seconds before the process tree is killed, the environment allowlist, and the
/// runner's JSON failure line.</summary>
/// <remarks>
/// The runner's children inherit only <see cref="InheritedVariables"/>; the gateways set the <c>WHEELHOUSE_*</c> and
/// <c>REHEARSAL_STATE</c> values per run, so an ambient one can never point the runner at another rig, API or token.
/// </remarks>
public static class RunnerProcess
{
    /// <summary>Holds the key of the runner's process client.</summary>
    public const string ClientName = "runner";

    /// <summary>Holds how long one runner call may run: within the console's request budget, past the runner's own SSH
    /// bounds (30 seconds a read, two targets a follow pass, about 90 seconds a migrations check).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(110);

    /// <summary>Holds the variables the runner inherits from the API's environment; everything else is withheld.</summary>
    /// <remarks>
    ///   - the SDK baseline: <c>PATH</c> (finds <c>python3</c>, <c>ssh</c>, <c>scp</c>, <c>ssh-keygen</c>), <c>HOME</c>,
    ///     <c>LANG</c>, <c>LC_ALL</c>, <c>LC_CTYPE</c>, <c>TMPDIR</c> (the runner's temporary folders), <c>TZ</c> and the
    ///     Windows equivalents
    ///   - <c>USER</c>, <c>LOGNAME</c>: the local account, as ssh and Python see it
    ///   - <c>SSH_AUTH_SOCK</c>: a developer's agent on the local rig; deployed consoles use the pinned key files
    ///   - <c>PYTHONPATH</c>, <c>PYTHONHOME</c>, <c>PYTHONIOENCODING</c>, <c>PYTHONUTF8</c>, <c>PYTHONDONTWRITEBYTECODE</c>:
    ///     the interpreter's own setup, when a host sets it
    ///   - <c>SSL_CERT_FILE</c>, <c>SSL_CERT_DIR</c>: the trust store the GitHub, GHCR and Tailscale calls verify against
    ///   - <c>HTTPS_PROXY</c>, <c>HTTP_PROXY</c>, <c>NO_PROXY</c> and their lowercase forms: an outbound proxy, when a host
    ///     needs one
    /// No <c>DOCKER_*</c> or <c>GIT_*</c>: the API-side runner calls neither; the target-side runner gets its environment from
    /// its SSH session on the target.
    /// </remarks>
    public static readonly IReadOnlyList<string> InheritedVariables =
    [
        .. ProcessEnvironmentConstants.Baseline,
        "USER", "LOGNAME",
        "SSH_AUTH_SOCK",
        "PYTHONPATH", "PYTHONHOME", "PYTHONIOENCODING", "PYTHONUTF8", "PYTHONDONTWRITEBYTECODE",
        "SSL_CERT_FILE", "SSL_CERT_DIR",
        "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "https_proxy", "http_proxy", "no_proxy",
    ];

    /// <summary>Holds the runner's failure line: one JSON object on the last standard-error line whose <c>reason</c> is
    /// display-safe; <c>"failure": "Rejected"</c> is a refused precondition, anything else a failed step.</summary>
    public static IProcessFailureParser FailureParser { get; } = new JsonFailureLineParser
    {
        Kinds = { ["Rejected"] = AppErrorType.Conflict },
    };

    /// <summary>Sets the runner client's bounds, allowlist and failure parser.</summary>
    /// <param name="options">The client's options.</param>
    public static void Configure(ProcessClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Timeout = Timeout;
        options.InheritedVariables = [.. InheritedVariables];
        options.FailureParser = FailureParser;
    }

    /// <summary>Describes one runner call: <paramref name="script"/> under the configured Python, its arguments, then
    /// <c>--root</c>.</summary>
    /// <param name="settings">The runner paths.</param>
    /// <param name="script">The runner script.</param>
    /// <param name="arguments">The action and its options.</param>
    /// <param name="environment">The values this call sets on top of the allowlist.</param>
    /// <param name="input">Values for the runner's standard input, never an argument.</param>
    /// <returns>The process spec.</returns>
    public static ProcessSpec Spec(
        DeploymentSettings settings,
        string script,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        string? input = null) => new()
    {
        FileName = settings.Python,
        Arguments = [script, .. arguments, "--root", settings.Root],
        Environment = environment,
        StandardInput = input,
    };
}
