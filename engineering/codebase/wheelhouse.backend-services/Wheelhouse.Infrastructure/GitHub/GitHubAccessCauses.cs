namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Words the Issues panel shows for each GitHub access problem; the owner is shown beside them.</summary>
public static class GitHubAccessCauses
{
    /// <summary>GitHub refused the App's own token.</summary>
    public const string AppRefused = "GitHub refused the GitHub App's credentials; check the App ID and the private key.";

    /// <summary>The key file is set but cannot be read.</summary>
    public const string KeyUnreadable = "The GitHub App private key could not be read.";

    /// <summary>The key file holds no usable RSA key.</summary>
    public const string KeyInvalid = "The GitHub App private key is not a valid RSA key in PEM form.";

    /// <summary>The App ID is set but no key is in place.</summary>
    public const string KeyMissing =
        "The GitHub App ID is set, but its private key is missing; every call falls back to the token file or anonymous access.";

    /// <summary>A key is in place but no App ID is set.</summary>
    public const string AppIdMissing = "The GitHub App private key is in place, but Deployment:GitHubAppId is not set.";

    /// <summary>Describes a refused GitHub call.</summary>
    /// <param name="status">The HTTP status: 401, 403 or 429.</param>
    /// <param name="authenticated">Whether the call carried a credential.</param>
    /// <param name="rateLimited">Whether GitHub said the rate limit ran out.</param>
    /// <param name="installed">Whether the call carried the owner's own App installation token.</param>
    public static string Refused(int status, bool authenticated, bool rateLimited, bool installed = true)
    {
        var what = (status, authenticated, rateLimited) switch
        {
            (401, _, _) => "GitHub refused the credential (HTTP 401).",
            (_, false, true) or (429, false, _) =>
                $"The anonymous rate limit ran out (HTTP {status}): 60 calls an hour, shared by this host.",
            (_, true, true) or (429, true, _) => $"The credential's rate limit ran out (HTTP {status}); calls resume when it resets.",
            (_, false, _) => $"GitHub refused an anonymous call (HTTP {status}).",
            _ => $"GitHub denied access (HTTP {status}): the credential lacks permission for this call.",
        };
        return installed ? what : what + " Install the GitHub App on this owner.";
    }

    /// <summary>Describes a repository no credential at hand could see: private, or gone.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    public static string Unseen(string repository) =>
        $"{repository} could not be read without the GitHub App; if it is private, install the App on this owner.";

    /// <summary>Describes a refused installation token.</summary>
    /// <param name="status">The HTTP status.</param>
    public static string InstallationRefused(int status) =>
        $"GitHub refused a token for this installation (HTTP {status}); it may be suspended or lack a permission.";

    /// <summary>Describes an image package no credential at hand could read, so a deploy that must pull it fails.</summary>
    /// <param name="repository">The <c>{owner}/{path}</c> image repository on GHCR.</param>
    /// <param name="product">The product whose release needs it, when the runner named one.</param>
    public static string PackageUnreadable(string repository, string? product) =>
        $"No credential can read the package ghcr.io/{repository}{(product is null ? "" : $" ({product})")}: a GitHub App "
        + "token cannot read a private repository's packages. Place a classic token with read:packages only in "
        + "<runner root>/github-token/token (or the file Deployment:GitHubTokenFile names).";
}
