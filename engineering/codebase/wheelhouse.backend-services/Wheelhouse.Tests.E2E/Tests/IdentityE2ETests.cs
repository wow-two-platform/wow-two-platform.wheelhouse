using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wheelhouse.Api.Auth;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the console's sign-in surface under <c>/api/identity</c>: who is signed in, signing out, and the GitHub
/// sign-in challenge — 503 until the OAuth app is configured. The SPA reads <c>me</c> to decide between the sign-in
/// screen and the dashboard, so its 401 and its JSON shape are the contract. The endpoints and the per-environment
/// session cookie come from the identity sdk; these tests pin what the console answers.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class IdentityE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task Me_ShouldReturn401AsProblem_WhenTheCallerIsAnonymous()
    {
        var response = await AnonymousClient.GetAsync("api/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Me_ShouldReturnTheSignedInOwner_WhenTheOwnerCalls()
    {
        var response = await AdminClient.GetAsync("api/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("login").GetString().Should().Be(TestAuthExtensions.AdminLogin);
        // No display name claim, so the name falls back to the login.
        body.RootElement.GetProperty("name").GetString().Should().Be(TestAuthExtensions.AdminLogin);
        body.RootElement.GetProperty("avatar").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SignOut_ShouldReturn401AsProblem_WhenTheCallerIsAnonymous()
    {
        var response = await AnonymousClient.PostAsync("api/identity/sign-out", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task SignOut_ShouldReturn204_WhenTheOwnerCalls()
    {
        var response = await AdminClient.PostAsync("api/identity/sign-out", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(null, ".wheelhouse.auth")]
    [InlineData("prod", ".wheelhouse.auth")]
    [InlineData("dev", ".wheelhouse.auth.dev")]
    public void SessionCookie_ShouldBeNamedPerEnvironment_SoDevAndProdOnOneHostNameKeepTheirOwnSignIn(
        string? environment, string expected)
    {
        // Cookies ignore the port: dev on :8443 and prod on :443 of one host name would overwrite each other's session.
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["DEPLOY_ENVIRONMENT"] = environment;
        builder.AddAuthentication();
        using var services = builder.Services.BuildServiceProvider();

        var cookie = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthConfigurationExtensions.CookieScheme).Cookie;

        cookie.Name.Should().Be(expected);
    }

    [Fact]
    public async Task SignIn_ShouldChallengeGitHub_OrAnswer503UntilTheOAuthAppIsConfigured()
    {
        // A developer's user-secrets may configure the OAuth app for this host; CI never does. Either way the answer
        // follows from whether the GitHub scheme was registered.
        var schemes = Fixture.Host.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var configured = await schemes.GetSchemeAsync(AuthConfigurationExtensions.GitHubScheme) is not null;
        using var client = Fixture.Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("api/identity/sign-in?returnUrl=%2Fproducts");

        if (configured)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be("https://github.com/login/oauth/authorize");
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Be(
            "GitHub sign-in is not configured. Set Identity:GitHub:ClientId and Identity:GitHub:ClientSecret (user-secrets).");
    }
}
