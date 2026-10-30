using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Fleets.Services;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for fleet rollouts: several releases Ready to promote go to prod in one run, one product at a time through the
/// ordinary deploy command, as the owner who started it; the fleet stops at the first failure or refusal (the rest are
/// not started), stops between products once cancelled, and answers for itself in the audit trail.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class FleetRolloutE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private static readonly string[] Products = ["foreverpin", "ocharo", "pbn"];

    [Fact]
    public async Task Fleet_ShouldPromoteEachProductInOrder_AndStopAtTheFirstFailure()
    {
        await SeedAsync();
        var started = await StartAsync(Items(Products), reason: "weekly release train");
        started.GetProperty("status").GetString().Should().Be("running");
        Statuses(started).Should().Equal("running", "waiting", "waiting");
        Fixture.Deployments.Starts.Should().Equal(("foreverpin-prod", "foreverpin-gh-2", "test-admin"));
        Fixture.Deployments.LastReason.Should().Be("weekly release train");
        Fixture.Deployments.LastFleet.Should().Be(Guid.Parse(started.GetProperty("id").GetString()!));
        var first = started.GetProperty("items")[0];
        first.GetProperty("fromRelease").GetString().Should().Be("v1.0.0");
        first.GetProperty("release").GetString().Should().Be("v1.1.0");

        (await AdvanceAsync()).Should().Be(1);
        Fixture.Deployments.Starts.Should().HaveCount(1, "the first product has no finished outcome yet");

        Finish(DeploymentOf(started, 0), "succeeded");
        await AdvanceAsync();
        var fleet = await FleetAsync(started);
        Statuses(fleet).Should().Equal("succeeded", "running", "waiting");
        Fixture.Deployments.Starts.Select(start => start.Target).Should().Equal("foreverpin-prod", "ocharo-prod");

        Finish(DeploymentOf(fleet, 1), "rolled_back", "compose up failed (exit 1)");
        await AdvanceAsync();
        fleet = await FleetAsync(started);
        fleet.GetProperty("status").GetString().Should().Be("failed");
        Statuses(fleet).Should().Equal("succeeded", "failed", "not_started");
        fleet.GetProperty("items")[1].GetProperty("detail").GetString().Should().Be("Rolled back: compose up failed (exit 1)");
        fleet.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String);
        Fixture.Deployments.Starts.Should().HaveCount(2, "the products after a failure are not started");

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray().ToList();
        var starts = audit.Where(entry => entry.GetProperty("action").GetString() == "deployment.start").ToList();
        starts.Should().HaveCount(2);
        starts.Select(entry => entry.GetProperty("actor").GetString()).Should().AllBe("test-admin",
            "a fleet's deploys are its owner's, the background ones too");
        starts.Should().AllSatisfy(entry => entry.GetProperty("detail").GetString().Should()
            .Contain(", fleet " + started.GetProperty("id").GetString()).And.EndWith(", reason: weekly release train"));
        var begun = audit.Single(entry => entry.GetProperty("action").GetString() == "deployment.fleet");
        begun.GetProperty("subject").GetString().Should().Be("portfolio");
        begun.GetProperty("detail").GetString().Should().StartWith("fleet " + started.GetProperty("id").GetString() + ": 3 products: ");
        var ended = audit.Single(entry => entry.GetProperty("action").GetString() == "deployment.fleet.end");
        ended.GetProperty("actor").GetString().Should().Be("test-admin");
        ended.GetProperty("outcome").GetString().Should().Be("failed");
        ended.GetProperty("detail").GetString().Should().Contain("failed at ocharo");

        var listed = (await DataAsync("/api/deployments/fleets")).EnumerateArray().Single();
        listed.GetProperty("id").GetString().Should().Be(started.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Fleet_ShouldEndSucceeded_WhenEveryProductSucceeds()
    {
        await SeedAsync();
        var started = await StartAsync(Items(["ocharo", "foreverpin"]));
        Fixture.Deployments.Starts.Select(start => start.Target).Should().Equal("ocharo-prod");
        Finish(DeploymentOf(started, 0), "succeeded");
        await AdvanceAsync();
        var fleet = await FleetAsync(started);
        Finish(DeploymentOf(fleet, 1), "succeeded");
        await AdvanceAsync();

        fleet = await FleetAsync(started);
        fleet.GetProperty("status").GetString().Should().Be("succeeded");
        Statuses(fleet).Should().Equal("succeeded", "succeeded");
        (await AdvanceAsync()).Should().Be(0, "an ended fleet is not looked at again");
    }

    [Fact]
    public async Task Fleet_ShouldStopBetweenProducts_WhenCancelled()
    {
        await SeedAsync();
        var started = await StartAsync(Items(["foreverpin", "ocharo"]));

        var cancelled = await SendAsync(HttpMethod.Post, $"/api/deployments/fleets/{started.GetProperty("id").GetString()}/cancel");
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var fleet = (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        fleet.GetProperty("status").GetString().Should().Be("running", "the product it is on finishes first");
        fleet.GetProperty("cancelRequestedBy").GetString().Should().Be("test-admin");

        Finish(DeploymentOf(started, 0), "succeeded");
        await AdvanceAsync();
        fleet = await FleetAsync(started);
        fleet.GetProperty("status").GetString().Should().Be("cancelled");
        Statuses(fleet).Should().Equal("succeeded", "not_started");
        Fixture.Deployments.Starts.Should().HaveCount(1);

        (await SendAsync(HttpMethod.Post, $"/api/deployments/fleets/{started.GetProperty("id").GetString()}/cancel"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "an ended fleet cannot be cancelled");
        (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Count(entry => entry.GetProperty("action").GetString() == "deployment.fleet.cancel").Should().Be(2,
                "the refused second cancel is audited too");
    }

    [Fact]
    public async Task Fleet_ShouldStop_WhenAGateRefusesAProduct()
    {
        await SeedAsync();
        Fixture.Deployments.RefuseTarget = ("ocharo-prod", "Deployment rejected: Another deployment owns this target");
        var started = await StartAsync(Items(Products));
        Finish(DeploymentOf(started, 0), "succeeded");
        await AdvanceAsync();

        var fleet = await FleetAsync(started);
        fleet.GetProperty("status").GetString().Should().Be("failed");
        Statuses(fleet).Should().Equal("succeeded", "refused", "not_started");
        fleet.GetProperty("items")[1].GetProperty("detail").GetString().Should().Contain("Another deployment owns this target");
    }

    [Fact]
    public async Task Fleet_ShouldRefuseWhatIsNotReadyToPromote_AndRunOneAtATime()
    {
        await SeedAsync();
        (await PostAsync(new { items = new[] { new { target = "foreverpin-prod", release = "foreverpin-gh-9" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "the release was never proven on dev");
        (await PostAsync(new { items = new[] { new { target = "foreverpin-dev", release = "foreverpin-gh-2" } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a fleet promotes to prod only");
        (await PostAsync(new { items = new[] { new { target = "nowhere-prod", release = "foreverpin-gh-2" } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(new { items = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(new { items = new[] { Item("foreverpin"), Item("foreverpin") } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "each prod target goes once");
        Fixture.Deployments.Starts.Should().BeEmpty();

        await StartAsync(Items(["foreverpin"]));
        (await PostAsync(new { items = new[] { Item("ocharo") } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "one fleet runs at a time");
    }

    [Fact]
    public async Task Fleet_ShouldRefuseAProductWheelhouseDoesNotDeploy()
    {
        await SeedAsync();
        await ProductDeployModes.SetAsync(AdminClient, "ocharo", "ci");

        (await PostAsync(new { items = Items(Products) }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "its own CI deploys ocharo, so Ready to promote never offers it");
        Fixture.Deployments.Starts.Should().BeEmpty();
        Statuses(await StartAsync(Items(["foreverpin", "pbn"]))).Should().Equal("running", "waiting");
    }

    [Fact]
    public async Task Fleet_ShouldHonorFreezes_UnlessOverriddenWithAReason()
    {
        await SeedAsync();
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "freeze");
        (await client.PostAsJsonAsync("/api/deployments/freezes", new { product = "ocharo", environment = "prod", reason = "billing" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await PostAsync(new { items = Items(Products) });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("ocharo-prod");
        (await PostAsync(new { items = Items(Products), overrideFreeze = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "an override needs a reason");
        Fixture.Deployments.Starts.Should().BeEmpty();

        var started = await StartAsync(Items(Products), reason: "hotfix through the freeze", overrideFreeze: true);
        Finish(DeploymentOf(started, 0), "succeeded");
        await AdvanceAsync();
        Fixture.Deployments.Starts.Select(start => start.Target).Should().Equal("foreverpin-prod", "ocharo-prod");
        Statuses(await FleetAsync(started)).Should().Equal("succeeded", "running", "waiting");
    }

    /// <summary>Adds Ocharo and PBN beside ForeverPin, each with dev and prod; prod runs v1.0.0 and dev ran v1.1.0.</summary>
    private async Task SeedAsync()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var productsRepository = services.GetRequiredService<IProductsRepository>();
            var workspace = (await productsRepository.GetAllAsync()).Single(row => row.Slug == "foreverpin").WorkspaceId;
            var servers = (await services.GetRequiredService<IServersRepository>().GetAllAsync()).ToDictionary(row => row.Slug);
            var targets = services.GetRequiredService<ITargetsRepository>();
            foreach (var slug in Products.Skip(1))
            {
                var product = await productsRepository.CreateAsync(new ProductEntity
                {
                    Id = Guid.NewGuid(), Slug = slug, Name = slug, Description = "", Repository = "owner/" + slug,
                    DefaultBranch = "main", WorkspaceId = workspace,
                });
                await targets.CreateAsync(Target(slug + "-dev", product, servers["pilot-host"].Id, DeploymentEnvironment.Dev));
                await targets.CreateAsync(Target(slug + "-prod", product, servers["prod-host"].Id, DeploymentEnvironment.Prod));
            }
        }

        var now = Fixture.Host.Clock.GetUtcNow();
        string At(TimeSpan before) => (now - before).ToString("O", CultureInfo.InvariantCulture);
        var index = 0;
        foreach (var slug in Products)
        {
            Fixture.Deployments.History.Add(new
            {
                id = $"00000000-0000-4000-8000-{++index:D12}", targetId = slug + "-prod", bundleId = slug + "-gh-1", release = "v1.0.0",
                status = "succeeded", kind = "release", submittedAt = At(TimeSpan.FromDays(2)), completedAt = At(TimeSpan.FromDays(2)),
                publishedAt = At(TimeSpan.FromDays(3)),
            });
            Fixture.Deployments.History.Add(new
            {
                id = $"00000000-0000-4000-8000-{++index:D12}", targetId = slug + "-dev", bundleId = slug + "-gh-2", release = "v1.1.0",
                status = "succeeded", kind = "release", submittedAt = At(TimeSpan.FromHours(2)), completedAt = At(TimeSpan.FromHours(2)),
                publishedAt = At(TimeSpan.FromHours(3)),
            });
        }
    }

    private static TargetEntity Target(string slug, ProductEntity product, Guid server, DeploymentEnvironment environment) => new()
    {
        Id = Guid.NewGuid(), Slug = slug, ProductId = product.Id, ServerId = server, Environment = environment,
        Network = "platform", Root = "/srv/wheelhouse",
        Settings = [new TargetSettingValueObject { Service = "api", Path = "/srv/settings/" + slug + "/api.json" }],
    };

    private static object Item(string product) => new { target = product + "-prod", release = product + "-gh-2" };

    private static object[] Items(IEnumerable<string> products) => [.. products.Select(Item)];

    /// <summary>Records the rollout's finished outcome where the history read finds it.</summary>
    private void Finish(string id, string status, string? reason = null)
    {
        var now = Fixture.Host.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        Fixture.Deployments.History.Add(new
        {
            id, targetId = Fixture.Deployments.Starts.Last().Target, bundleId = Fixture.Deployments.Starts.Last().Release,
            release = "v1.1.0", status, reason, kind = "release", submittedAt = now, completedAt = now,
        });
    }

    private static string DeploymentOf(JsonElement fleet, int position) =>
        fleet.GetProperty("items")[position].GetProperty("deploymentId").GetString()!;

    private static IEnumerable<string?> Statuses(JsonElement fleet) =>
        fleet.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString());

    private async Task<JsonElement> StartAsync(object[] items, string? reason = null, bool overrideFreeze = false)
    {
        var response = await PostAsync(new { items, reason, overrideFreeze });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private Task<HttpResponseMessage> PostAsync(object body)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        return client.PostAsJsonAsync("/api/deployments/fleets", body);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        return client.SendAsync(new HttpRequestMessage(method, path));
    }

    private Task<JsonElement> FleetAsync(JsonElement fleet) => DataAsync("/api/deployments/fleets/" + fleet.GetProperty("id").GetString());

    /// <summary>Runs the background pass, outside any request.</summary>
    private async Task<int> AdvanceAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FleetRolloutService>().AdvanceRunningAsync(CancellationToken.None);
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
