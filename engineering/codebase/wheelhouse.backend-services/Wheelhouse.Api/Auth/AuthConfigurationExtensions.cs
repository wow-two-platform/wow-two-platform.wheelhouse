using Wheelhouse.Domain.Integrations.Constants;
using Wheelhouse.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication.Cookies;
using WoW.Two.Sdk.Backend.Beta.Foundation.Configuration;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;
using WoW.Two.Sdk.Backend.Beta.Identity.Authorization;
using WoW.Two.Sdk.Backend.Beta.Identity.Claims;
using WoW.Two.Sdk.Backend.Beta.Identity.Cookies;
using WoW.Two.Sdk.Backend.Beta.Identity.OAuth.GitHub;

namespace Wheelhouse.Api.Auth;

/// <summary>Wires single-admin GitHub sign-in on the SDK identity primitives — cookie session, claim normalization, login allowlist, and a default-deny API.</summary>
public static class AuthConfigurationExtensions
{
    /// <summary>The cookie session scheme issued after a successful sign-in.</summary>
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>The GitHub OAuth challenge scheme.</summary>
    public const string GitHubScheme = "GitHub";

    /// <summary>The path GitHub redirects back to after authorization.</summary>
    public const string CallbackPath = "/api/identity/callback";

    /// <summary>The authorization policy protected endpoints require.</summary>
    public const string AdminPolicy = "WheelhouseAdmin";

    /// <summary>The policy catalog reads require: the operator's session, or an integration key granting the scope.</summary>
    public const string ProductsReadPolicy = "ProductsRead";

    /// <summary>The marker every integration key secret starts with, so a leaked one is recognizable.</summary>
    public const string KeyMarker = "wh_";

    /// <summary>Binds the identity settings and wires cookie auth, GitHub OAuth, claim normalization, the login allowlist, and default-deny authorization.</summary>
    /// <param name="builder">The web application builder.</param>
    public static WebApplicationBuilder AddAuthentication(this WebApplicationBuilder builder)
    {
        // Bind both settings through the SDK loader: section bind + environment-variable overlay (+ required validation).
        // GitHubOAuthSettings is also registered as IOptions<T> — IdentityController reads it to gate sign-in on IsConfigured.
        builder.Services.AddEnvironmentOverlaidOptions<GitHubOAuthSettings>(builder.Configuration, "Identity:GitHub");

        var gitHub = ConfigurationMapper.Load<GitHubOAuthSettings>(builder.Configuration, "Identity:GitHub");
        var authSettings = ConfigurationMapper.Load<AuthSettings>(builder.Configuration, "Identity");
        if (builder.Environment.IsProduction() && !authSettings.AllowedGitHubLogins.Any(login => !string.IsNullOrWhiteSpace(login)))
            throw new InvalidOperationException("Production requires Identity:AllowedGitHubLogins.");

        // Cookie holds the session; API mode returns 401/403 (not a 302) so the SPA renders its own sign-in.
        builder.Services.AddCookieAuthentication(o =>
        {
            o.Mode = AuthChallengeMode.Api;
            o.CookieName = ".wheelhouse.auth";
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
        });

        // GitHub is the login provider — registered only when configured, so the host boots with empty creds.
        if (gitHub.IsConfigured)
        {
            builder.Services.AddAuthentication().AddGitHubAuthentication(
                gitHub.ClientId,
                gitHub.ClientSecret,
                configure: o =>
                {
                    o.CallbackPath = CallbackPath;
                    o.SignInScheme = CookieScheme;
                },
                "user:email", "repo", "read:packages");
        }

        // Normalize provider claims to wt:* so the allowlist and dashboard read one shape regardless of provider.
        builder.Services.AddClaimNormalization();

        // Allowlist keyed on the normalized username; empty (default) = open (self-hosted — the runner is the user).
        builder.Services.AddPrincipalAllowlist(o =>
        {
            foreach (var login in authSettings.AllowedGitHubLogins)
                o.Allowed.Add(login);
        });

        // Every endpoint requires the signed-in, allowlisted admin by default; /health opts out via [AllowAnonymous].
        builder.Services.AddDefaultDenyAuthorization(CookieScheme, withAllowlist: true);

        // Integration keys: another program reads the catalog with a scoped key. A key reaches only an endpoint whose
        // policy names the key scheme; the default-deny fallback above stays cookie-only. A catalog read admits the
        // allowlisted session or a key granting catalog:read; a key never passes as a session.
        builder.Services.AddApiKeyAuthentication(keys => keys.Marker = KeyMarker);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ProductsReadPolicy, policy => policy
                .RequireAllowlistedSessionOrApiKeyScope(IntegrationScopeConstants.CatalogRead, CookieScheme));

        return builder;
    }

    /// <summary>Adds authentication and authorization middleware; the SDK pipeline places it after routing and before
    /// idempotency.</summary>
    /// <param name="app">The application pipeline.</param>
    public static IApplicationBuilder UseWheelhouseAuth(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
