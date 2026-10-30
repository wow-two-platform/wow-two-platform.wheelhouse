using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for service controls (decision 45): the owner stops, starts or restarts one service, or stops and starts a whole
/// environment, or resets its database (v0.9); prod needs its target ID typed and a reason; Wheelhouse's own
/// environments, integration keys and a target a running fleet still promotes are refused; each control is audited and
/// reads in the history beside the deploys without counting as one, and a stopped environment reads as stopped.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ServiceControlsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string StopId = "00000000-0000-4000-8000-000000000c01";
    private const string FailedDeployId = "00000000-0000-4000-8000-000000000c02";
    private const string LiveId = "00000000-0000-4000-8000-000000000c03";

    [Fact]
    public async Task Owner_ShouldStopAServiceOnDev_WithAPlainConfirm_AndTheAuditNamesIt()
    {
        var response = await ControlAsync("foreverpin-dev", new { operation = "stop", service = "management" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-dev", "stop", "management", "test-admin", (string?)null));

        (await ControlAsync("foreverpin-dev", new { operation = "start" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.Controls[^1].Should().Be(("foreverpin-dev", "start", (string?)null, "test-admin", (string?)null));

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray().ToList();
        audit.Should().Contain(entry => entry.GetProperty("action").GetString() == "service.stop"
            && entry.GetProperty("subject").GetString() == "foreverpin-dev/management");
        audit.Should().Contain(entry => entry.GetProperty("action").GetString() == "environment.start"
            && entry.GetProperty("subject").GetString() == "foreverpin-dev");
    }

    [Fact]
    public async Task Prod_ShouldNeedItsTargetIdTypedAndAReason()
    {
        var untyped = await ControlAsync("foreverpin-prod", new { operation = "restart", service = "management", reason = "stuck" });
        untyped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await untyped.Content.ReadAsStringAsync()).Should().Contain("Type foreverpin-prod to restart prod");

        var silent = await ControlAsync("foreverpin-prod", new { operation = "stop", confirm = "foreverpin-prod" });
        silent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await silent.Content.ReadAsStringAsync()).Should().Contain("Give a reason to stop prod");
        Fixture.Deployments.Controls.Should().BeEmpty("nothing reaches the runner before both are given");

        var accepted = await ControlAsync("foreverpin-prod",
            new { operation = "restart", service = "management", confirm = "foreverpin-prod", reason = " cache stuck " });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-prod", "restart", "management", "test-admin", "cache stuck"));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .First(entry => entry.GetProperty("action").GetString() == "service.restart");
        audit.GetProperty("detail").GetString().Should().Be("reason: cache stuck");
    }

    [Fact]
    public async Task Reset_ShouldReachTheRunnerOnDev_AndProdNeedsItsTargetIdTypedAndAReason()
    {
        var dev = await ControlAsync("foreverpin-dev", new { operation = "reset" });
        dev.StatusCode.Should().Be(HttpStatusCode.Accepted, await dev.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-dev", "reset", (string?)null, "test-admin", (string?)null));

        var untyped = await ControlAsync("foreverpin-prod", new { operation = "reset", reason = "schema drifted" });
        untyped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await untyped.Content.ReadAsStringAsync()).Should().Contain("Type foreverpin-prod to reset prod");
        var silent = await ControlAsync("foreverpin-prod", new { operation = "reset", confirm = "foreverpin-prod" });
        silent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await silent.Content.ReadAsStringAsync()).Should().Contain("Give a reason to reset prod");
        Fixture.Deployments.Controls.Should().HaveCount(1, "nothing reaches the runner before both are given");

        var accepted = await ControlAsync("foreverpin-prod",
            new { operation = "reset", confirm = "foreverpin-prod", reason = "schema drifted" });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls[^1].Should().Be(("foreverpin-prod", "reset", (string?)null, "test-admin", "schema drifted"));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "database.reset").ToList();
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-dev");
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-prod"
            && entry.GetProperty("detail").GetString() == "reason: schema drifted");
    }

    [Fact]
    public async Task History_ShouldReadAReset_AndLeaveNothingStoppedBehindIt()
    {
        const string id = "00000000-0000-4000-8000-000000000c05";
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id = StopId, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "succeeded",
            operation = new { kind = "stop", services = Array.Empty<string>() },
            stopped = new { environment = true, services = Array.Empty<string>(), at = Instant(now.AddHours(-2)), actor = "test-admin" },
            submittedAt = Instant(now.AddHours(-2)), completedAt = Instant(now.AddHours(-2).AddSeconds(20)),
        });
        Fixture.Deployments.History.Add(new
        {
            id, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "succeeded",
            deployReason = "schema drifted", operation = new { kind = "reset", services = Array.Empty<string>() }, stopped = (object?)null,
            submittedAt = Instant(now.AddHours(-1)), completedAt = Instant(now.AddHours(-1).AddSeconds(40)),
        });

        var item = (await DataAsync("/api/deployments/history")).GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == id);
        (item.GetProperty("operation").GetProperty("kind").GetString(), item.GetProperty("state").GetString())
            .Should().Be(("reset", "succeeded"));
        var dev = (await DataAsync("/api/portfolio/summary?weeks=2")).GetProperty("products").EnumerateArray()
            .Single(product => product.GetProperty("product").GetString() == "foreverpin").GetProperty("environments")
            .EnumerateArray().Single(row => row.GetProperty("environment").GetString() == "dev");
        dev.GetProperty("stopped").ValueKind.Should().Be(JsonValueKind.Null, "the reset hands every service to the next deploy");
    }

    [Theory]
    [InlineData("{\"operation\":\"reset\",\"service\":\"management\"}", "never one service")]
    [InlineData("{\"operation\":\"restart\"}", "Restart one service at a time")]
    [InlineData("{\"operation\":\"stop\",\"service\":\"Management;rm\"}", "Service")]
    [InlineData("{\"operation\":\"pause\"}", "operation")]
    [InlineData("{}", "Operation")]
    public async Task Control_ShouldRefuseAMalformedRequest(string body, string message)
    {
        var client = ActionClient();
        var response = await client.PostAsync("/api/deployments/targets/foreverpin-dev/controls",
            new StringContent(body, new MediaTypeHeaderValue("application/json")));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().ContainEquivalentOf(message);
        Fixture.Deployments.Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Control_ShouldAnswerNotFound_ForAnUnknownTarget() =>
        (await ControlAsync("nowhere-dev", new { operation = "stop" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task Wheelhouse_ShouldNeverStopItself()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var product = await services.GetRequiredService<IProductsRepository>().GetBySlugAsync("wheelhouse");
            var control = await services.GetRequiredService<IServersRepository>().CreateAsync(new ServerEntity
            {
                Id = Guid.NewGuid(), Slug = "control", Name = "Control host", Provider = VpsProvider.Ovhcloud,
                Host = "172.30.0.1", Region = "bhs", SshUser = "wheelhouse-deploy", SshPort = 22, Ingress = new ServerIngressValueObject(),
            });
            await services.GetRequiredService<ITargetsRepository>().CreateAsync(new TargetEntity
            {
                Id = Guid.NewGuid(), Slug = "wheelhouse-dev", ProductId = product!.Id, ServerId = control.Id,
                Environment = DeploymentEnvironment.Dev, Network = "wheelhouse", Root = "/srv/wheelhouse",
            });
        }

        foreach (var body in new object[] { new { operation = "stop" }, new { operation = "restart", service = "console" }, new { operation = "reset" } })
        {
            var refused = await ControlAsync("wheelhouse-dev", body);
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("runs Wheelhouse itself");
        }
        Fixture.Deployments.Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task Control_ShouldWaitForAFleetThatStillPromotesTheTarget()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IFleetRolloutsRepository>().AddAsync(new FleetRolloutEntity
            {
                Id = Guid.NewGuid(), Actor = "test-admin", Status = FleetRolloutStatus.Running, CreatedAt = Fixture.Host.Clock.GetUtcNow(),
                Items = [new FleetRolloutItemEntity { Product = "foreverpin", Target = "foreverpin-prod", BundleId = "foreverpin-gh-2", Status = FleetItemStatus.Waiting }],
            });

        var refused = await ControlAsync("foreverpin-prod", new { operation = "stop", confirm = "foreverpin-prod", reason = "maintenance" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("A fleet rollout is promoting foreverpin-prod");
        (await ControlAsync("foreverpin-dev", new { operation = "stop" })).StatusCode.Should().Be(HttpStatusCode.Accepted,
            "dev is no part of the fleet");
    }

    [Fact]
    public async Task Control_ShouldPassTheRunnersRefusalOn()
    {
        Fixture.Deployments.ControlRefusal = "A rollout or another control is running on foreverpin-dev";
        var refused = await ControlAsync("foreverpin-dev", new { operation = "stop" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("another control is running");
    }

    [Fact]
    public async Task Control_ShouldBeTheOwnersAlone()
    {
        var anonymous = AnonymousClient;
        anonymous.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        (await anonymous.PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/controls", new { operation = "stop" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var key = await KeyClientAsync();
        key.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        (await key.PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/controls", new { operation = "stop" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await AdminClient.PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/controls", new { operation = "stop" }))
            .StatusCode.Should().NotBe(HttpStatusCode.Accepted, "a write without its action header is refused");
        Fixture.Deployments.Controls.Should().BeEmpty();
    }

    [Fact]
    public async Task History_ShouldListAControlBesideTheDeploys_WithoutCountingItAsOne()
    {
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id = FailedDeployId, targetId = "foreverpin-dev", bundleId = "foreverpin-gh-3", release = "v1.1.0", actor = "automation",
            status = "failed", mutationStarted = true, reason = "compose up failed (exit 1)", product = "foreverpin",
            submittedAt = Instant(now.AddHours(-3)), completedAt = Instant(now.AddHours(-3).AddMinutes(2)),
        });
        Fixture.Deployments.History.Add(new
        {
            id = StopId, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "succeeded",
            deployReason = "quiet night", operation = new { kind = "stop", services = Array.Empty<string>() },
            stopped = new { environment = true, services = Array.Empty<string>(), at = Instant(now.AddHours(-1)), actor = "test-admin" },
            submittedAt = Instant(now.AddHours(-1)), completedAt = Instant(now.AddHours(-1).AddSeconds(20)),
        });
        Fixture.Deployments.History.Add(new
        {
            id = LiveId, targetId = "foreverpin-prod", release = "v1.0.0", actor = "test-admin", status = "running",
            operation = new { kind = "restart", services = new[] { "management" } }, submittedAt = Instant(now.AddMinutes(-1)),
        });

        var items = (await DataAsync("/api/deployments/history")).GetProperty("items").EnumerateArray().ToList();
        var stop = items.Single(item => item.GetProperty("id").GetString() == StopId);
        stop.GetProperty("operation").GetProperty("kind").GetString().Should().Be("stop");
        stop.GetProperty("state").GetString().Should().Be("succeeded");
        stop.GetProperty("deployReason").GetString().Should().Be("quiet night");
        items.Select(item => item.GetProperty("id").GetString()).Should().ContainInOrder(LiveId, StopId, FailedDeployId);

        var service = (await DataAsync("/api/deployments/history?service=foreverpin/management")).GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToList();
        service.Should().Contain([StopId, LiveId], "a whole-environment stop and a restart of the service both touched it");

        (await DataAsync("/api/deployments/running")).EnumerateArray().Select(item => item.GetProperty("id").GetString())
            .Should().Equal(LiveId);
        (await DataAsync("/api/deployments/" + StopId + "/detail")).GetProperty("deployment").GetProperty("operation")
            .GetProperty("kind").GetString().Should().Be("stop");

        // The failed deploy still needs a look: a later stop is no success that clears it.
        (await DataAsync("/api/deployments/issues")).EnumerateArray()
            .Should().Contain(issue => issue.GetProperty("deployment").GetProperty("id").GetString() == FailedDeployId);
        var stats = await DataAsync("/api/deployments/history/stats");
        stats.ToString().Should().NotContain(StopId);
    }

    [Fact]
    public async Task FailedControl_ShouldReadWithTheReasonItsTargetGave()
    {
        // ForeverPin's dev names a host settings file for management, as the canary does on wheelhouse-dev.
        const string id = "00000000-0000-4000-8000-000000000c04";
        const string reason = "No settings file for management is left on this target; deploy the release again";
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "failed", failure = "Rejected",
            reason, operation = new { kind = "restart", services = new[] { "management" } },
            submittedAt = Instant(now.AddMinutes(-2)), completedAt = Instant(now.AddMinutes(-2).AddSeconds(3)),
        });

        var item = (await DataAsync("/api/deployments/history")).GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == id);
        (item.GetProperty("state").GetString(), item.GetProperty("attention").GetString()).Should().Be(("failed", "act"));
        item.GetProperty("cause").GetProperty("message").GetString().Should().Be(reason);
        item.GetProperty("operation").GetProperty("kind").GetString().Should().Be("restart");
        (await DataAsync("/api/deployments/" + id + "/detail")).GetProperty("deployment").GetProperty("reason").GetString()
            .Should().Be(reason);
    }

    [Fact]
    public async Task Portfolio_ShouldReadAStoppedEnvironmentAsStopped()
    {
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id = StopId, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "succeeded",
            operation = new { kind = "stop", services = new[] { "management" } },
            stopped = new { environment = false, services = new[] { "management" }, at = Instant(now.AddHours(-1)), actor = "test-admin" },
            submittedAt = Instant(now.AddHours(-1)), completedAt = Instant(now.AddHours(-1).AddSeconds(20)),
        });

        var summary = await DataAsync("/api/portfolio/summary?weeks=2");
        var environments = summary.GetProperty("products").EnumerateArray()
            .Single(product => product.GetProperty("product").GetString() == "foreverpin").GetProperty("environments")
            .EnumerateArray().ToList();
        var dev = environments.Single(row => row.GetProperty("environment").GetString() == "dev");
        dev.GetProperty("stopped").GetProperty("services").EnumerateArray().Select(name => name.GetString()).Should().Equal("management");
        dev.GetProperty("lastDeploy").ValueKind.Should().Be(JsonValueKind.Null, "a control is no deploy");
        environments.Single(row => row.GetProperty("environment").GetString() == "prod").GetProperty("stopped").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    private HttpClient ActionClient()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        return client;
    }

    private Task<HttpResponseMessage> ControlAsync(string target, object body) =>
        ActionClient().PostAsJsonAsync("/api/deployments/targets/" + target + "/controls", body);

    private async Task<HttpClient> KeyClientAsync()
    {
        var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var created = await admin.PostAsJsonAsync("/api/integration-keys", new { name = "Claude", scopes = new[] { "deployments:read" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private static string Instant(DateTimeOffset value) => value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
