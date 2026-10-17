using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the single-host SPA contract (<c>HostConfiguration</c>): the React shell is served at <c>/</c> and as the
/// fallback for unknown non-<c>/api</c> routes, while an unmatched <c>/api/*</c> path must 404 as JSON — never the SPA
/// HTML (an HTML body for an API path is cacheable and broke clients, the products cache-confusion bug). The shell is
/// anonymous so the sign-in screen loads before auth.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class SpaServingE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task GetRoot_ServesSpaShell()
    {
        // Anonymous on purpose — the dashboard shell must load before sign-in.
        var response = await AnonymousClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<div id=\"root\">");
    }

    [Fact]
    public async Task GetUnknownNonApiRoute_FallsBackToSpaShell()
    {
        // A deep client-side route the server doesn't know — SPA fallback returns index.html so the router takes over.
        var response = await AnonymousClient.GetAsync("/servers/some-client-route");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<div id=\"root\">");
    }

    [Fact]
    public async Task GetUnknownApiRoute_Returns404AsJson_NotSpaShell()
    {
        var response = await AnonymousClient.GetAsync("/api/this-endpoint-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Load-bearing: the body is a JSON problem document, never the SPA HTML shell.
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        mediaType.Should().NotBe("text/html");
        mediaType.Should().Contain("json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("<div id=\"root\">");
        body.Should().NotContain("<!doctype html");
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    [InlineData("/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/security.txt")]
    public async Task GetWellKnownPath_Returns404Json_NotSpaShell(string path)
    {
        // MCP clients probe OAuth discovery here; an HTML 200 would read as a broken authorization server.
        var response = await AnonymousClient.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("<div id=\"root\">");
    }

    [Fact]
    public async Task GetUnknownApiRoute_WithHtmlAccept_StillReturns404Json()
    {
        // Even when the caller prefers HTML, an /api/* miss must not be answered with the SPA shell.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/nope/nested");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await AnonymousClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("<div id=\"root\">");
    }
}
