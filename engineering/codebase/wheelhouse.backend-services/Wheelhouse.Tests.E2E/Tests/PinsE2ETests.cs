using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Pins.Enums;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for pins — the signed-in operator keeps workspaces, products and services one click away in a custom order.
/// Pinning a target twice answers the first pin, a pin goes with what it points at, an operator sees only their own
/// pins, integration keys are refused, and nothing reaches the audit trail.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class PinsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<IReadOnlyList<PinResponse>> ListAsync() =>
        await (await AdminClient.GetAsync("api/pins")).ReadEnvelopeAsync<IReadOnlyList<PinResponse>>();

    private async Task<PinResponse> PinAsync(object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await ActionClient("pin").PostJsonAsync("api/pins", body);
        response.StatusCode.Should().Be(expected);
        return await response.ReadEnvelopeAsync<PinResponse>();
    }

    private async Task<Guid> WorkspaceIdAsync() =>
        (await (await AdminClient.GetAsync("api/workspaces")).ReadEnvelopeAsync<IReadOnlyList<WorkspaceResponse>>())[0].Id;

    private async Task<Guid> ProductIdAsync(string slug) =>
        (await (await AdminClient.GetAsync("api/products/" + slug)).ReadEnvelopeAsync<ProductResponse>()).Id;

    [Fact]
    public async Task GetAll_ShouldBeEmpty_WhenTheOperatorPinnedNothing()
    {
        var response = await AdminClient.GetAsync("api/pins");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<IReadOnlyList<PinResponse>>()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ShouldPinEachKindLastInTheOrder()
    {
        var workspaceId = await WorkspaceIdAsync();
        var foreverPin = await ProductIdAsync("foreverpin");

        var workspace = await PinAsync(new { kind = "workspace", workspaceId });
        var product = await PinAsync(new { kind = "product", productId = foreverPin });
        var service = await PinAsync(new { kind = "service", productId = foreverPin, service = "redirect" });

        workspace.Should().BeEquivalentTo(new PinResponse(workspace.Id, "workspace", workspaceId, null, null, 0));
        product.Should().BeEquivalentTo(new PinResponse(product.Id, "product", null, foreverPin, null, 1));
        service.Should().BeEquivalentTo(new PinResponse(service.Id, "service", null, foreverPin, "redirect", 2));
        (await ListAsync()).Select(pin => pin.Id).Should().Equal(workspace.Id, product.Id, service.Id);
    }

    [Fact]
    public async Task Create_ShouldAnswerTheExistingPin_WhenTheTargetIsPinnedAgain()
    {
        var foreverPin = await ProductIdAsync("foreverpin");
        var first = await PinAsync(new { kind = "service", productId = foreverPin, service = "management" });

        // The slug names the same product, so the same target.
        var again = await PinAsync(new { kind = "service", productId = "foreverpin", service = "management" }, HttpStatusCode.OK);

        again.Should().Be(first);
        (await ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Create_ShouldKeepAProductPinAndItsServicePinsApart()
    {
        var wheelhouse = await ProductIdAsync("wheelhouse");

        await PinAsync(new { kind = "product", productId = wheelhouse });
        await PinAsync(new { kind = "service", productId = wheelhouse, service = "console" });
        await PinAsync(new { kind = "service", productId = wheelhouse, service = "runner" });

        (await ListAsync()).Select(pin => (pin.Kind, pin.Service)).Should()
            .Equal(("product", null), ("service", "console"), ("service", "runner"));
    }

    [Fact]
    public async Task Create_ShouldReturn400_WhenTheBodyDoesNotNameTheTargetItsKindTakes()
    {
        var workspaceId = await WorkspaceIdAsync();
        var client = ActionClient("pin");

        foreach (var body in new object[]
        {
            new { kind = "workspace" },
            new { kind = "workspace", workspaceId, productId = "foreverpin" },
            new { kind = "product", workspaceId },
            new { kind = "product", productId = "foreverpin", service = "api" },
            new { kind = "service", productId = "foreverpin" },
            new { kind = "service", productId = "foreverpin", service = "Not_A_Key" },
            new { kind = "environment", productId = "foreverpin" },
        })
            (await client.PostJsonAsync("api/pins", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, body.ToString());

        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturn400_WhenTheTargetDoesNotExist()
    {
        var client = ActionClient("pin");

        (await client.PostJsonAsync("api/pins", new { kind = "workspace", workspaceId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostJsonAsync("api/pins", new { kind = "product", productId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostJsonAsync("api/pins", new { kind = "service", productId = "ghost", service = "api" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_ShouldRemoveThePin_AndReturn404ForAnUnknownOne()
    {
        var pin = await PinAsync(new { kind = "product", productId = "wheelhouse" });

        (await ActionClient("pin").DeleteAsync("api/pins/" + pin.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActionClient("pin").DeleteAsync("api/pins/" + pin.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Pins_ShouldStayTheirOwnersAlone()
    {
        var mine = await PinAsync(new { kind = "product", productId = "wheelhouse" });
        Guid theirs;
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var pins = scope.ServiceProvider.GetRequiredService<IPinsRepository>();
            theirs = (await pins.CreateAsync(new PinEntity
            {
                Id = Guid.NewGuid(), Owner = "someone-else", Kind = PinKind.Product, ProductId = await ProductIdAsync("foreverpin"), Order = 0,
            })).Id;
        }

        (await ListAsync()).Select(pin => pin.Id).Should().Equal(mine.Id);
        (await ActionClient("pin").DeleteAsync("api/pins/" + theirs)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ActionClient("pin").PutJsonAsync("api/pins/order", new { ids = new[] { theirs, mine.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        // The other operator's pin does not take this operator's next place.
        (await PinAsync(new { kind = "product", productId = "foreverpin" })).Order.Should().Be(1);
    }

    [Fact]
    public async Task Reorder_ShouldReturnTheNewOrder_WhenEveryPinIsNamedOnce()
    {
        var a = await PinAsync(new { kind = "product", productId = "wheelhouse" });
        var b = await PinAsync(new { kind = "product", productId = "foreverpin" });
        var c = await PinAsync(new { kind = "workspace", workspaceId = await WorkspaceIdAsync() });

        var response = await ActionClient("pin").PutJsonAsync("api/pins/order", new { ids = new[] { c.Id, a.Id, b.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<IReadOnlyList<PinResponse>>()).Select(pin => (pin.Id, pin.Order))
            .Should().Equal((c.Id, 0), (a.Id, 1), (b.Id, 2));
        (await ListAsync()).Select(pin => pin.Id).Should().Equal(c.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task Reorder_ShouldBeRefused_WhenAPinIsMissingUnknownOrRepeated()
    {
        var a = await PinAsync(new { kind = "product", productId = "wheelhouse" });
        var b = await PinAsync(new { kind = "product", productId = "foreverpin" });
        var client = ActionClient("pin");

        (await client.PutJsonAsync("api/pins/order", new { ids = new[] { b.Id } })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutJsonAsync("api/pins/order", new { ids = new[] { b.Id, a.Id, Guid.NewGuid() } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutJsonAsync("api/pins/order", new { ids = new[] { b.Id, b.Id } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListAsync()).Select(pin => pin.Id).Should().Equal(a.Id, b.Id);
    }

    [Fact]
    public async Task Pins_ShouldGoWithTheProductOrWorkspaceTheyPointAt()
    {
        var created = await ActionClient("workspace").PostJsonAsync("api/workspaces", new { slug = "personal", name = "Personal" });
        var personal = await created.ReadEnvelopeAsync<WorkspaceResponse>();
        await PinAsync(new { kind = "workspace", workspaceId = personal.Id });
        await PinAsync(new { kind = "product", productId = "wheelhouse" });
        await PinAsync(new { kind = "service", productId = "wheelhouse", service = "console" });
        var kept = await PinAsync(new { kind = "product", productId = "foreverpin" });

        (await ActionClient("product").DeleteAsync("api/products/wheelhouse")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActionClient("workspace").DeleteAsync("api/workspaces/" + personal.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync()).Select(pin => pin.Id).Should().Equal(kept.Id);
    }

    [Fact]
    public async Task Endpoints_ShouldRefuseIntegrationKeysWith403_AndAnonymousCallersWith401()
    {
        var created = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "Reader", scopes = new[] { "catalog:read", "deployments:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var key = AnonymousClient;
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        key.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "pin");
        var anonymous = AnonymousClient;
        anonymous.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "pin");

        (await key.GetAsync("api/pins")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await key.PostJsonAsync("api/pins", new { kind = "product", productId = "wheelhouse" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("api/pins")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostJsonAsync("api/pins", new { kind = "product", productId = "wheelhouse" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Writes_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var pin = await PinAsync(new { kind = "product", productId = "wheelhouse" });

        (await AdminClient.PostJsonAsync("api/pins", new { kind = "product", productId = "foreverpin" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.PutJsonAsync("api/pins/order", new { ids = new[] { pin.Id } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.DeleteAsync("api/pins/" + pin.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Writes_ShouldLeaveTheAuditTrailAlone_SincePinsAreAPreference()
    {
        var a = await PinAsync(new { kind = "product", productId = "wheelhouse" });
        var b = await PinAsync(new { kind = "product", productId = "foreverpin" });
        (await ActionClient("pin").PutJsonAsync("api/pins/order", new { ids = new[] { b.Id, a.Id } })).EnsureSuccessStatusCode();
        (await ActionClient("pin").DeleteAsync("api/pins/" + a.Id)).EnsureSuccessStatusCode();

        var audit = await AdminClient.GetFromJsonAsync<JsonElement>("api/audit");
        audit.GetProperty("data").GetArrayLength().Should().Be(0);
    }
}
