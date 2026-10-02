using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Api.Auth;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;
using WoW.Two.Sdk.Backend.Beta.Testing.Auth;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Configures the SDK test-auth scheme as Wheelhouse's allowlisted admin and re-points the admin policies at it.</summary>
/// <remarks>Wheelhouse's policies name the cookie scheme, so making the test scheme the default is not enough: the admin,
/// fallback and catalog-read policies are registered again against it. Clients without <see cref="AdminHeader"/> stay
/// anonymous, so protected endpoints return 401.</remarks>
public static class TestAuthExtensions
{
    /// <summary>Holds the request header that signs a request in as the admin; any value works.</summary>
    public const string AdminHeader = "X-Test-Admin";

    /// <summary>Holds the GitHub login the test admin carries, which the host's allowlist names.</summary>
    public const string AdminLogin = "test-admin";

    /// <summary>Registers the SDK test-auth scheme as the admin and rebinds Wheelhouse's policies onto it. Call from a
    /// test host's services hook so it overrides the real cookie and OAuth registration.</summary>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection UseTestAdminAuth(this IServiceCollection services)
    {
        services.AddTestAuth(options =>
        {
            options.UserId = "test-admin-id";
            options.Name = AdminLogin;
            options.RequiredHeader = AdminHeader;
            // The login as the claim normalizer writes it, which the allowlist reads.
            options.ExtraClaims = [new Claim("wt:username", AdminLogin)];
        });

        var adminPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName).RequireAuthenticatedUser().Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthConfigurationExtensions.AdminPolicy, adminPolicy)
            .SetFallbackPolicy(adminPolicy)
            // Catalog reads keep the real integration key scheme beside the test session.
            .AddPolicy(AuthConfigurationExtensions.ProductsReadPolicy, policy => policy
                .AddAuthenticationSchemes(TestAuthHandler.SchemeName, ApiKeyAuthenticationDefaults.Scheme)
                .AddRequirements(new ProductsReadRequirement()));

        return services;
    }
}
