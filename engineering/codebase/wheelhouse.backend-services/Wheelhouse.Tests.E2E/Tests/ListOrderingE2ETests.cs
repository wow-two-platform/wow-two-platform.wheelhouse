using System.Net;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for list ordering — the integration key collection returns rows newest-first (<c>CreatedAt</c> descending), the
/// order the console relies on.
/// </summary>
/// <remarks>
/// The host runs on the harness <c>FakeTimeProvider</c> (<see cref="WheelhouseAppFixture.Host"/><c>.Clock</c>), so without
/// intervention every row would stamp the same instant and the sort would be a no-op. Each insert advances the fake
/// clock, giving rows distinct, deterministic timestamps — the order is then asserted exactly, with zero wall-clock
/// flakiness.
/// </remarks>
[Collection(WheelhouseCollection.Name)]
public sealed class ListOrderingE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    /// <summary>Per-insert clock advance so each row gets its own timestamp tick.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task GetKeys_ShouldReturnNewestFirst_WhenCreatedInSequence()
    {
        // Created oldest → newest; the response must come back newest → oldest.
        var creationOrder = new[] { "order-a", "order-b", "order-c", "order-d" };
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        foreach (var name in creationOrder)
        {
            var created = await client.PostJsonAsync("api/integration-keys", new { name, scopes = new[] { "catalog:read" } });
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            Fixture.Host.Clock.Advance(Tick);
        }

        var response = await AdminClient.GetAsync("api/integration-keys");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await response.ReadEnvelopeAsync<IReadOnlyList<IntegrationKeyResponse>>();

        keys.Should().HaveCount(creationOrder.Length);
        keys.Select(key => key.CreatedAt).Should().BeInDescendingOrder();
        keys.Select(key => key.Name).Should().Equal(creationOrder.Reverse());
    }
}
