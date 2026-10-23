namespace Wheelhouse.Infrastructure.Settings;

/// <summary>Holds owner-configured runner paths; HTTP clients cannot choose them.</summary>
/// <remarks>Relative paths resolve against the API's content root, so a local run can point at the repository.</remarks>
public sealed record DeploymentSettings
{
    /// <summary>The rig switch value that leaves the local rehearsal fleet hidden.</summary>
    public const string RigOff = "";

    /// <summary>The API runs on the developer's machine and reaches the rig through its published loopback ports.</summary>
    public const string RigHost = "host";

    /// <summary>The API runs inside the rig and reaches the target and vault by service name.</summary>
    public const string RigNetwork = "network";

    /// <summary>Gets or sets the executable used for the Python runner.</summary>
    public string Python { get; set; } = "python3";
    /// <summary>Gets or sets the installed SSH adapter path.</summary>
    public string TransportPath { get; set; } = "/app/runner/transport.py";
    /// <summary>Gets or sets an optional mounted GitHub token path: the fallback credential for an owner without the
    /// GitHub App, and the only one that reads a private repository's packages (a classic <c>read:packages</c> token).
    /// Empty means <c>{Root}/github-token/token</c> once the owner places it (<see cref="TokenFile"/>).</summary>
    public string GitHubTokenFile { get; set; } = "";
    /// <summary>Gets or sets the GitHub App's ID (or client ID); empty leaves the App off. Host configuration only.</summary>
    public string GitHubAppId { get; set; } = "";
    /// <summary>Gets or sets the GitHub App's private key (PEM) path; empty means <c>{Root}/github-app/private-key.pem</c>.
    /// A missing file leaves the App off and says so in the Issues panel.</summary>
    public string GitHubAppKeyFile { get; set; } = "";
    /// <summary>Gets or sets the protected inventory, release and audit root.</summary>
    public string Root { get; set; } = "/data/deployments";
    /// <summary>Gets or sets how the local rehearsal rig is exposed: empty (off, always in a deployed control plane), <c>host</c> or <c>network</c>.</summary>
    public string Rehearsal { get; set; } = RigOff;
    /// <summary>Gets or sets the rig's state folder as the Docker host sees it; needed only when the API runs inside the rig.</summary>
    public string RehearsalState { get; set; } = "";

    /// <summary>Gets or sets how many seconds one read of the runner's deployment history serves the console's reads;
    /// zero reads it every time.</summary>
    public int HistorySeconds { get; set; } = 3;

    /// <summary>The token file's place under the runner root when <see cref="GitHubTokenFile"/> names none.</summary>
    public static readonly string DefaultTokenFile = Path.Combine("github-token", "token");

    /// <summary>Gets the token file a call reads now: the configured one, else the default under the runner root while
    /// it exists, else empty; checked at each call, so placing the file needs no restart.</summary>
    public string TokenFile()
    {
        if (GitHubTokenFile.Length > 0)
            return GitHubTokenFile;
        var placed = Root.Length == 0 ? "" : Path.Combine(Root, DefaultTokenFile);
        return placed.Length > 0 && File.Exists(placed) ? placed : "";
    }

    /// <summary>Gets whether a GitHub App ID is configured; its key file may still be missing.</summary>
    public bool HasGitHubApp => GitHubAppId.Length > 0;

    /// <summary>Gets whether the local rehearsal rig is exposed.</summary>
    public bool IsLocalRig => Rehearsal is RigHost or RigNetwork;
}
