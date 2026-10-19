namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Represents the bearer token one GitHub call carries; never logged or returned.</summary>
/// <param name="Token">The token.</param>
/// <param name="Source">Where it came from.</param>
public sealed record GitHubCredential(string Token, GitHubCredentialSource Source)
{
    /// <inheritdoc />
    public override string ToString() => $"GitHubCredential {{ Source = {Source} }}";
}
