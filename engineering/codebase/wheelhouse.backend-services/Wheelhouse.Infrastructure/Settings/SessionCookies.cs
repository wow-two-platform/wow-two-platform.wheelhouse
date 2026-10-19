namespace Wheelhouse.Infrastructure.Settings;

/// <summary>Names the console's session cookie for the environment it serves.</summary>
public static class SessionCookies
{
    /// <summary>The session cookie's name for prod and for a local run.</summary>
    public const string SessionCookie = ".wheelhouse.auth";

    /// <summary>Names the session cookie for the environment a console serves. Cookies ignore the port, so dev and prod
    /// on one host name would overwrite each other's sign-in: prod keeps the plain name, any other deployed environment
    /// adds its own.</summary>
    /// <param name="deployEnvironment">The runner's <c>DEPLOY_ENVIRONMENT</c>; empty on a local run.</param>
    /// <returns>The cookie name, such as <c>.wheelhouse.auth</c> or <c>.wheelhouse.auth.dev</c>.</returns>
    public static string SessionCookieName(string? deployEnvironment)
    {
        var environment = deployEnvironment?.Trim().ToLowerInvariant() ?? "";
        if (environment.Length == 0 || environment == "prod" || !environment.All(char.IsAsciiLetterOrDigit))
            return SessionCookie;
        return SessionCookie + "." + environment;
    }
}
