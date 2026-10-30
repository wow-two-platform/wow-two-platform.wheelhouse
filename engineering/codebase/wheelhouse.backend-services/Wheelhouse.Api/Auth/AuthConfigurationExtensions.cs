using Wheelhouse.Domain.Integrations.Constants;
using Wheelhouse.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Claims;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.OAuth.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.OAuth.GitHub;

namespace Wheelhouse.Api.Auth;

/// <summary>Wires single-admin GitHub sign-in on the SDK identity primitives — cookie session, claim normalization, login allowlist, and a default-deny API.</summary>
public static class AuthConfigurationExtensions
{
    /// <summary>The cookie session scheme issued after a successful sign-in.</summary>
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>The GitHub OAuth challenge scheme.</summary>
    public const string GitHubScheme = "GitHub";

    /// <summary>The route group of the sign-in endpoints: sign-in, me, sign-out and GitHub's callback.</summary>
    public const string IdentityPath = "/api/identity";

    /// <summary>The path GitHub redirects back to after authorization.</summary>
    public const string CallbackPath = IdentityPath + "/callback";

    /// <summary>The session cookie's name for prod and for a local run; any other deployed environment adds its own
    /// suffix (<c>.wheelhouse.auth.dev</c>), since dev and prod share one host name and cookies ignore the port.</summary>
    public const string SessionCookie = ".wheelhouse.auth";

    /// <summary>What sign-in answers, as a 503 problem, until the OAuth app is configured.</summary>
    public const string NotConfiguredDetail =
        "GitHub sign-in is not configured. Set Identity:GitHub:ClientId and Identity:GitHub:ClientSecret (user-secrets).";

    /// <summary>The authorization policy protected endpoints require.</summary>
    public const string AdminPolicy = "WheelhouseAdmin";

    /// <summary>The policy catalog reads require: the owner's session, or an integration key granting the scope.</summary>
    public const string ProductsReadPolicy = "ProductsRead";

    /// <summary>The policy the owner's own preferences require: the allowlisted session. An integration key is
    /// authenticated so it can be told apart, then refused with 403; anonymous callers get 401.</summary>
    public const string OwnerSessionPolicy = "OwnerSession";

    /// <summary>The marker every integration key secret starts with, so a leaked one is recognizable.</summary>
    public const string KeyMarker = "wh_";

    /// <summary>Binds the identity settings and wires cookie auth, GitHub OAuth, claim normalization, the login allowlist, and default-deny authorization.</summary>
    /// <param name="builder">The web application builder.</param>
    public static WebApplicationBuilder AddAuthentication(this WebApplicationBuilder builder)
    {
        // Bind both settings through the SDK loader: section bind + environment-variable overlay (+ required validation).
        var gitHub = ConfigurationMapper.Load<GitHubOAuthSettings>(builder.Configuration, "Identity:GitHub");
        var authSettings = ConfigurationMapper.Load<AuthSettings>(builder.Configuration, "Identity");

        // Cookie holds the session; API mode returns 401/403 (not a 302) so the SPA renders its own sign-in.
        builder.Services.AddCookieAuthentication(o =>
        {
            o.Mode = AuthChallengeMode.Api;
            o.CookieName = SessionCookie;
            o.Environment = builder.Configuration.GetDeployEnvironment();
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
        });

        // GitHub is the login provider — registered only when configured, so the host boots with empty creds and
        // sign-in answers 503 (MapIdentityEndpoints).
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
                .RequireAllowlistedSessionOrApiKeyScope(IntegrationScopeConstants.CatalogRead, CookieScheme))
            .AddPolicy(OwnerSessionPolicy, policy => policy.RequireOwnerSession(CookieScheme));

        return builder;
    }

    /// <summary>Requires an allowlisted owner's session on <paramref name="sessionScheme"/>. The integration key scheme
    /// authenticates too, so a key is forbidden (403) rather than challenged: a key names a program, never the owner.</summary>
    /// <param name="policy">The policy builder.</param>
    /// <param name="sessionScheme">The scheme the owner's session signs in with.</param>
    /// <returns>The same builder for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireOwnerSession(this AuthorizationPolicyBuilder policy, string sessionScheme) =>
        policy
            .AddAuthenticationSchemes(sessionScheme, ApiKeyAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.GetApiKeyName() is null)
            .AddRequirements(new AllowlistRequirement());

    /// <summary>Refuses to serve a deployed console without a login allowlist: an empty one admits any GitHub user, so
    /// only a local run may have one. Deployed dev runs as Development, so the runner's DEPLOY_ENVIRONMENT, not the
    /// environment name, tells a deployed console. Checked as the pipeline is built, so a <c>migrate</c> or <c>vault</c>
    /// command, which never serves, runs without the console's settings.</summary>
    /// <param name="app">The built application.</param>
    /// <returns>The same application for chaining.</returns>
    /// <exception cref="InvalidOperationException">A deployed console names no allowed login.</exception>
    public static WebApplication RequireLoginAllowlist(this WebApplication app)
    {
        var authSettings = ConfigurationMapper.Load<AuthSettings>(app.Configuration, "Identity");
        if ((app.Environment.IsProduction() || app.Configuration.IsDeployed())
            && !authSettings.AllowedGitHubLogins.Any(login => !string.IsNullOrWhiteSpace(login)))
            throw new InvalidOperationException("A deployed console requires Identity:AllowedGitHubLogins.");
        return app;
    }

    /// <summary>Maps the SDK sign-in endpoints under <see cref="IdentityPath"/>: <c>sign-in</c> (a GitHub challenge, 503
    /// until configured), <c>me</c> and <c>sign-out</c>.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application for chaining.</returns>
    public static WebApplication MapIdentityEndpoints(this WebApplication app)
    {
        app.MapGroup(IdentityPath).MapOAuthSignInEndpoints(GitHubScheme, o => o.NotConfiguredDetail = NotConfiguredDetail);
        return app;
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
