using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace Wheelhouse.Infrastructure.Settings;

/// <summary>Configuration for the GitHub OAuth application used to sign the single admin in.
/// Loaded via <see cref="ConfigurationLoader"/>: bound from <c>Identity:GitHub</c>, then overlaid from the
/// <c>GITHUB_OAUTH_*</c> environment variables (the deploy-time source for the secret).</summary>
/// <example>Identity:GitHub</example>
public sealed record GitHubOAuthSettings
{
    /// <summary>Gets the GitHub OAuth app client id. Empty until configured (user-secrets locally, env in deploy).</summary>
    /// <example>Iv1.abc123def456</example>
    [EnvironmentVariable("GITHUB_OAUTH_CLIENT_ID")]
    public string ClientId { get; init; } = "";

    /// <summary>Gets the GitHub OAuth app client secret. Never committed — user-secrets locally, env in deploy.</summary>
    /// <example>0123456789abcdef0123456789abcdef01234567</example>
    [EnvironmentVariable("GITHUB_OAUTH_CLIENT_SECRET")]
    public string ClientSecret { get; init; } = "";

    /// <summary>Gets a value indicating whether GitHub OAuth is configured (both id and secret present).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
