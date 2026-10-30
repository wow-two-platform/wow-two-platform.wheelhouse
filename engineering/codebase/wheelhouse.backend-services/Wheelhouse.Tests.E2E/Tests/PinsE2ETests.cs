using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Pins.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for pins — the signed-in owner keeps products one click away in a custom order; workspace and service pins are
/// refused (products only since 2026-10-18). Pinning a product twice answers the first pin, a pin goes with its product,
/// the owner sees only their own pins, integration keys are refused, and nothing reaches the audit trail.
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

    /// <summary>Adds a third product, so an order has room to change.</summary>
    private async Task<Guid> AddProductAsync(string slug)
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var workspace = await WorkspaceIdAsync();
        return (await scope.ServiceProvider.GetRequiredService<IProductsRepository>().CreateAsync(new ProductEntity
        {
            Id = Guid.NewGuid(), Slug = slug, Name = slug, Description = "", Repository = "owner/" + slug,
            DefaultBranch = "main", WorkspaceId = workspace,
        })).Id;
    }

    private async Task<Guid> ProductIdAsync(string slug) =>
        (await (await AdminClient.GetAsync("api/products/" + slug)).ReadEnvelopeAsync<ProductResponse>()).Id;

    [Fact]
    public async Task GetAll_ShouldBeEmpty_WhenTheOwnerPinnedNothing()
    {
        var response = await AdminClient.GetAsync("api/pins");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<IReadOnlyList<PinResponse>>()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ShouldPinEachProductLastInTheOrder()
    {
        var foreverPin = await ProductIdAsync("foreverpin");
        var wheelhouse = await ProductIdAsync("wheelhouse");

        var first = await PinAsync(new { kind = "product", productId = foreverPin });
        var second = await PinAsync(new { kind = "product", productId = "wheelhouse" });

        first.Should().BeEquivalentTo(new PinResponse(first.Id, "product", null, foreverPin, null, 0));
        second.Should().BeEquivalentTo(new PinResponse(second.Id, "product", null, wheelhouse, null, 1));
        (await ListAsync()).Select(pin => pin.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Create_ShouldAnswerTheExistingPin_WhenTheProductIsPinnedAgain()
    {
        var foreverPin = await ProductIdAsync("foreverpin");
        var first = await PinAsync(new { kind = "product", productId = foreverPin });

        // The slug names the same product, so the same pin.
        var again = await PinAsync(new { kind = "product", productId = "foreverpin" }, HttpStatusCode.OK);

        again.Should().Be(first);
        (await ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Create_ShouldRefuseWorkspaceAndServicePins_SincePinsAreProductsOnly()
    {
        var client = ActionClient("pin");

        foreach (var body in new object[]
        {
            new { kind = "workspace", workspaceId = await WorkspaceIdAsync() },
            new { kind = "service", productId = "foreverpin", service = "redirect" },
        })
        {
            var response = await client.PostJsonAsync("api/pins", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body.ToString());
            (await response.Content.ReadAsStringAsync()).Should().Contain("Only products are pinned");
        }

        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturn400_WhenTheBodyDoesNotNameTheTargetItsKindTakes()
    {
        var workspaceId = await WorkspaceIdAsync();
        var client = ActionClient("pin");

        foreach (var body in new object[]
        {
            new { kind = "product" },
            new { kind = "product", productId = "Not A Slug" },
            new { kind = "product", workspaceId },
            new { kind = "product", productId = "foreverpin", service = "api" },
            new { kind = "environment", productId = "foreverpin" },
        })
            (await client.PostJsonAsync("api/pins", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, body.ToString());

        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturn400_WhenTheProductDoesNotExist()
    {
        var client = ActionClient("pin");

        (await client.PostJsonAsync("api/pins", new { kind = "product", productId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostJsonAsync("api/pins", new { kind = "product", productId = "ghost" }))
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
        // The other owner's pin does not take this owner's next place.
        (await PinAsync(new { kind = "product", productId = "foreverpin" })).Order.Should().Be(1);
    }

    [Fact]
    public async Task Reorder_ShouldReturnTheNewOrder_WhenEveryPinIsNamedOnce()
    {
        var a = await PinAsync(new { kind = "product", productId = "wheelhouse" });
        var b = await PinAsync(new { kind = "product", productId = "foreverpin" });
        var c = await PinAsync(new { kind = "product", productId = await AddProductAsync("pilot") });

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
    public async Task Pins_ShouldGoWithTheProductTheyPointAt()
    {
        await PinAsync(new { kind = "product", productId = "wheelhouse" });
        var kept = await PinAsync(new { kind = "product", productId = "foreverpin" });

        (await ActionClient("product").DeleteAsync("api/products/wheelhouse")).StatusCode.Should().Be(HttpStatusCode.NoContent);

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

        (await AdminClient.AuditItemsAsync()).GetArrayLength().Should().Be(0);
    }
}
