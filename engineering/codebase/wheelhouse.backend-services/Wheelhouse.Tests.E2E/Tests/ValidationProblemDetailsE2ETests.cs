using System.Net;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for invalid input over HTTP — a rejected command must come back as an RFC 7807 <c>ProblemDetails</c>
/// (<c>title</c>/<c>status</c>, content negotiated by the host), NOT the success <c>ApiResponse&lt;T&gt;</c> envelope.
/// Asserting the failure shape end-to-end is something only the real host pipeline can prove.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ValidationProblemDetailsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    [Fact]
    public async Task CreateKey_ShouldReturn400ProblemDetails_WhenTheNameIsEmpty()
    {
        var response = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "", scopes = new[] { "catalog:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Status.Should().Be(400);
        problem.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateKey_ShouldReturn400ProblemDetails_WhenNoScopeIsChosen()
    {
        var response = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "Claude", scopes = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Status.Should().Be(400);
    }

    [Fact]
    public async Task UpdateLifecycle_ShouldReturn400ProblemDetails_WhenTheBodyIsMissingTheLifecycle()
    {
        var response = await ActionClient("lifecycle").PutJsonAsync("api/products/foreverpin/lifecycle", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Status.Should().Be(400);
    }
}
