namespace Wheelhouse.Infrastructure.Settings;

/// <summary>Optional allowlist that locks who may sign in. Empty (default) = <b>open</b>: any authenticated
/// GitHub user is admitted (self-hosted — the runner is the user). Populate only to restrict an exposed instance.
/// Loaded via <see cref="WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigurationLoader"/> from the <c>Identity</c> section.</summary>
/// <example>Identity</example>
public sealed record AuthSettings
{
    /// <summary>Gets the GitHub logins permitted to sign in. Empty = open (anyone with a GitHub account); when set, only these pass.</summary>
    /// <remarks>A list, so it stays a config-only value (no single-string env overlay) — set it in <c>appsettings</c> / user-secrets.</remarks>
    /// <example>["my-github-handle"]</example>
    public IReadOnlyList<string> AllowedGitHubLogins { get; init; } = [];
}
