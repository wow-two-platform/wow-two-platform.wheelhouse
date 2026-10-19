namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Represents an installation token GitHub issued for one owner; never logged or returned.</summary>
/// <param name="Token">The token.</param>
/// <param name="ExpiresAt">When GitHub stops accepting it, an hour after issue.</param>
public sealed record GitHubAppToken(string Token, DateTimeOffset ExpiresAt)
{
    /// <inheritdoc />
    public override string ToString() => $"GitHubAppToken {{ ExpiresAt = {ExpiresAt:O} }}";
}
