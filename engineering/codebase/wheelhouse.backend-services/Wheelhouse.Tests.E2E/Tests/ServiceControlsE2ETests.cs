using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Products.Entities;
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
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-dev", "stop", "management", "test-admin", (string?)null, (string?)null));

        (await ControlAsync("foreverpin-dev", new { operation = "start" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.Controls[^1].Should().Be(("foreverpin-dev", "start", (string?)null, "test-admin", (string?)null, (string?)null));

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
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-prod", "restart", "management", "test-admin", "cache stuck", (string?)null));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .First(entry => entry.GetProperty("action").GetString() == "service.restart");
        audit.GetProperty("detail").GetString().Should().Be("reason: cache stuck");
    }

    [Fact]
    public async Task Reset_ShouldReachTheRunnerOnDev_AndProdNeedsItsTargetIdTypedAndAReason()
    {
        var dev = await ControlAsync("foreverpin-dev", new { operation = "reset" });
        dev.StatusCode.Should().Be(HttpStatusCode.Accepted, await dev.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-dev", "reset", (string?)null, "test-admin", (string?)null, (string?)null));

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
        Fixture.Deployments.Controls[^1].Should().Be(("foreverpin-prod", "reset", (string?)null, "test-admin", "schema drifted", (string?)null));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "database.reset").ToList();
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-dev");
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-prod"
            && entry.GetProperty("detail").GetString() == "reason: schema drifted");
    }

    [Fact]
    public async Task SetReset_ShouldReachTheRunnerWithItsSet_AndProdNeedsItsTargetIdTypedAndAReason()
    {
        var dev = await ControlAsync("foreverpin-dev", new { operation = "reset", set = "identity" });
        dev.StatusCode.Should().Be(HttpStatusCode.Accepted, await dev.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-dev", "reset", (string?)null, "test-admin", (string?)null, "identity"));
        (await dev.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("operation").GetProperty("set")
            .GetString().Should().Be("identity");

        var untyped = await ControlAsync("foreverpin-prod", new { operation = "reset", set = "identity", reason = "identity drifted" });
        untyped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await untyped.Content.ReadAsStringAsync()).Should().Contain("Type foreverpin-prod to reset prod");
        var silent = await ControlAsync("foreverpin-prod", new { operation = "reset", set = "identity", confirm = "foreverpin-prod" });
        silent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await silent.Content.ReadAsStringAsync()).Should().Contain("Give a reason to reset prod");
        Fixture.Deployments.Controls.Should().HaveCount(1, "nothing reaches the runner before both are given");

        var accepted = await ControlAsync("foreverpin-prod",
            new { operation = "reset", set = "identity", confirm = "foreverpin-prod", reason = "identity drifted" });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls[^1].Should().Be(("foreverpin-prod", "reset", (string?)null, "test-admin", "identity drifted", "identity"));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "migration-set.reset").ToList();
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-dev/identity");
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-prod/identity"
            && entry.GetProperty("detail").GetString() == "reason: identity drifted");
    }

    [Fact]
    public async Task Copy_ShouldReachTheRunnerWithItsSource_AndTheAuditNamesIt()
    {
        await AddHarvestAsync();

        var dev = await ControlAsync("harvest-dev", new { operation = "copy", from = "foreverpin-dev" });
        dev.StatusCode.Should().Be(HttpStatusCode.Accepted, await dev.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("harvest-dev", "copy", (string?)null, "test-admin", (string?)null, (string?)null));
        Fixture.Deployments.Copies.Should().Equal(("harvest-dev", "foreverpin-dev"));
        (await dev.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("operation").GetProperty("from")
            .GetString().Should().Be("foreverpin-dev");

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "database.copy");
        (audit.GetProperty("subject").GetString(), audit.GetProperty("detail").GetString())
            .Should().Be(("harvest-dev", "from: foreverpin-dev"));
    }

    [Fact]
    public async Task Copy_ShouldNeedProdsTargetIdTypedAndAReason_LikeAReset()
    {
        await AddHarvestAsync();

        var untyped = await ControlAsync("harvest-prod", new { operation = "copy", from = "foreverpin-prod", reason = "move data" });
        untyped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await untyped.Content.ReadAsStringAsync()).Should().Contain("Type harvest-prod to copy prod");
        var silent = await ControlAsync("harvest-prod", new { operation = "copy", from = "foreverpin-prod", confirm = "harvest-prod" });
        silent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await silent.Content.ReadAsStringAsync()).Should().Contain("Give a reason to copy prod");
        Fixture.Deployments.Controls.Should().BeEmpty("nothing reaches the runner before both are given");

        var accepted = await ControlAsync("harvest-prod",
            new { operation = "copy", from = "foreverpin-prod", confirm = "harvest-prod", reason = " move data " });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("harvest-prod", "copy", (string?)null, "test-admin", "move data", (string?)null));
        Fixture.Deployments.Copies.Should().Equal(("harvest-prod", "foreverpin-prod"));
        (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "database.copy").Should()
            .Contain(entry => entry.GetProperty("subject").GetString() == "harvest-prod"
                && entry.GetProperty("detail").GetString() == "from: foreverpin-prod; reason: move data");
    }

    [Fact]
    public async Task Copy_ShouldRefuseASourceOnAnotherServer_WheelhouseItself_TheSameEnvironmentAndAnUnknownOne()
    {
        var (pilot, foreverPin) = await AddHarvestAsync();
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var wheelhouse = await services.GetRequiredService<IProductsRepository>().GetBySlugAsync("wheelhouse");
            var targets = services.GetRequiredService<ITargetsRepository>();
            await targets.CreateAsync(Target("wheelhouse-dev", wheelhouse!.Id, pilot, DeploymentEnvironment.Dev));
            await targets.CreateAsync(Target("foreverpin-dev2", foreverPin, pilot, DeploymentEnvironment.Dev));
        }

        var crossed = await ControlAsync("harvest-dev", new { operation = "copy", from = "foreverpin-prod" });
        crossed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await crossed.Content.ReadAsStringAsync()).Should()
            .Contain("A copy reads the source on the same platform PostgreSQL; foreverpin-prod is on another server");
        var self = await ControlAsync("harvest-dev", new { operation = "copy", from = "wheelhouse-dev" });
        self.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await self.Content.ReadAsStringAsync()).Should().Contain("wheelhouse-dev runs Wheelhouse itself");
        var twin = await ControlAsync("foreverpin-dev", new { operation = "copy", from = "foreverpin-dev2" });
        twin.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await twin.Content.ReadAsStringAsync()).Should().Contain("own product and environment");
        var unknown = await ControlAsync("harvest-dev", new { operation = "copy", from = "nowhere-dev" });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("There is no environment nowhere-dev to copy from");
        var into = await ControlAsync("wheelhouse-dev", new { operation = "copy", from = "harvest-dev" });
        into.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await into.Content.ReadAsStringAsync()).Should().Contain("its database is never replaced from the console");
        Fixture.Deployments.Controls.Should().BeEmpty("nothing reaches the runner while the source is refused");
    }

    [Fact]
    public async Task History_ShouldReadACopy_WithItsSourceAndWhatItCopied()
    {
        const string id = "00000000-0000-4000-8000-000000000c07";
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "succeeded",
            operation = new { kind = "copy", services = Array.Empty<string>(), from = "harvestadapters-dev" },
            copy = new { from = "harvestadapters-dev", source = "harvestadapters_dev", database = "foreverpin_dev", tables = 12, rows = 48210 },
            stopped = (object?)null, submittedAt = Instant(now.AddHours(-1)), completedAt = Instant(now.AddHours(-1).AddSeconds(50)),
        });
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000c08", targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin",
            status = "failed", operation = new { kind = "copy", services = Array.Empty<string>(), from = "harvestadapters-dev" },
            copy = "unreadable", submittedAt = Instant(now.AddHours(-2)), completedAt = Instant(now.AddHours(-2).AddSeconds(5)),
        });

        var items = (await DataAsync("/api/deployments/history")).GetProperty("items").EnumerateArray().ToList();
        var item = items.Single(entry => entry.GetProperty("id").GetString() == id);
        (item.GetProperty("operation").GetProperty("kind").GetString(), item.GetProperty("operation").GetProperty("from").GetString(),
            item.GetProperty("state").GetString()).Should().Be(("copy", "harvestadapters-dev", "succeeded"));
        var copy = item.GetProperty("copy");
        (copy.GetProperty("source").GetString(), copy.GetProperty("database").GetString(), copy.GetProperty("tables").GetInt64(),
            copy.GetProperty("rows").GetInt64()).Should().Be(("harvestadapters_dev", "foreverpin_dev", 12L, 48210L));
        items.Single(entry => entry.GetProperty("id").GetString() == "00000000-0000-4000-8000-000000000c08")
            .GetProperty("copy").ValueKind.Should().Be(JsonValueKind.Null, "an unreadable copy never drops its record");
        (await DataAsync("/api/deployments/" + id + "/detail")).GetProperty("deployment").GetProperty("copy").GetProperty("rows")
            .GetInt64().Should().Be(48210);
    }

    [Fact]
    public async Task Run_ShouldReachTheRunnerWithItsCommand_ProdNeedsItsTargetIdTypedAndAReason_AndTheAuditKeepsNoValue()
    {
        string[] command = ["dotnet", "Identity.Api.dll", "admin", "create", "--slug", "ocharo", "--phone", "+998901234567"];
        var dev = await ControlAsync("foreverpin-dev", new { operation = "run", service = "management", command });
        dev.StatusCode.Should().Be(HttpStatusCode.Accepted, await dev.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls.Should().Equal(("foreverpin-dev", "run", "management", "test-admin", (string?)null, (string?)null));
        Fixture.Deployments.Commands.Should().ContainSingle().Which.Should().Equal(command);

        var untyped = await ControlAsync("foreverpin-prod", new { operation = "run", service = "management", command, reason = "first owner" });
        untyped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await untyped.Content.ReadAsStringAsync()).Should().Contain("Type foreverpin-prod to run prod");
        var silent = await ControlAsync("foreverpin-prod", new { operation = "run", service = "management", command, confirm = "foreverpin-prod" });
        silent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await silent.Content.ReadAsStringAsync()).Should().Contain("Give a reason to run prod");
        var unnamed = await ControlAsync("foreverpin-dev", new { operation = "run", command });
        unnamed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var bare = await ControlAsync("foreverpin-dev", new { operation = "run", service = "management" });
        bare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bare.Content.ReadAsStringAsync()).Should().Contain("A run takes its command");
        Fixture.Deployments.Controls.Should().HaveCount(1, "nothing reaches the runner before the rules hold");

        var accepted = await ControlAsync("foreverpin-prod",
            new { operation = "run", service = "management", command, confirm = "foreverpin-prod", reason = "first owner" });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Controls[^1].Should().Be(("foreverpin-prod", "run", "management", "test-admin", "first owner", (string?)null));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "service.run").ToList();
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-prod/management"
            && entry.GetProperty("detail").GetString() == "command: dotnet Identity.Api.dll admin create --slug … --phone …; reason: first owner");
        audit.Select(entry => entry.GetRawText()).Should().NotContain(text => text.Contains("+998") || text.Contains("ocharo\""));
    }

    [Fact]
    public async Task Run_ShouldHandItsStdinFileToTheRunner_AndAuditOnlyItsSize()
    {
        string[] command = ["dotnet", "Events.Api.dll", "import", "ocharo-fashion"];
        var bundle = System.Text.Encoding.UTF8.GetBytes("{\"rows\":[{\"title\":\"PRIVATE_NOTE\"}]}");
        var input = Convert.ToBase64String(bundle);
        var accepted = await ControlAsync("foreverpin-dev", new { operation = "run", service = "management", command, input });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Inputs.Should().Equal(input);

        var stop = await ControlAsync("foreverpin-dev", new { operation = "stop", service = "management", input });
        stop.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await stop.Content.ReadAsStringAsync()).Should().Contain("Only a run takes a file on its stdin");
        foreach (var refused in new[] { "not base64!", Convert.ToBase64String(new byte[2 * 1024 * 1024 + 1]) })
        {
            var response = await ControlAsync("foreverpin-dev", new { operation = "run", service = "management", command, input = refused });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("base64 of 1 byte to 2 MiB");
        }
        Fixture.Deployments.Inputs.Should().HaveCount(1, "nothing reaches the runner before the rules hold");

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "service.run").ToList();
        audit.Should().Contain(entry => entry.GetProperty("detail").GetString()
            == "command: dotnet Events.Api.dll import ocharo-fashion; stdin file: " + bundle.Length + " bytes");
        audit.Select(entry => entry.GetRawText()).Should().NotContain(text => text.Contains("PRIVATE_NOTE") || text.Contains(input));
    }

    [Fact]
    public async Task Reveal_ShouldAnswerARunsLineOnce_UncachedAndUnaudited()
    {
        var job = Guid.NewGuid();
        var first = await ActionClient().PostAsync($"/api/deployments/targets/foreverpin-dev/controls/{job}/reveal", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("line").GetString()
            .Should().Contain("invitation=SECRET");
        Fixture.Deployments.Reveals.Should().Equal(("foreverpin-dev", job.ToString("D")));

        var again = await ActionClient().PostAsync($"/api/deployments/targets/foreverpin-dev/controls/{job}/reveal", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ActionClient().PostAsync($"/api/deployments/targets/nowhere-dev/controls/{job}/reveal", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "service.run.reveal").ToList();
        audit.Should().Contain(entry => entry.GetProperty("subject").GetString() == "foreverpin-dev"
            && entry.GetProperty("detail").GetString() == "deployment " + job.ToString("D"));
        audit.Select(entry => entry.GetRawText()).Should().NotContain(text => text.Contains("SECRET"));
    }

    [Fact]
    public async Task SetReset_ShouldPassTheRunnersRefusalOfAnUnknownSetOn()
    {
        Fixture.Deployments.ControlRefusal = "The release v1.0.0 has no migration set nope";
        var refused = await ControlAsync("foreverpin-dev", new { operation = "reset", set = "nope" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("has no migration set nope");
    }

    [Fact]
    public async Task History_ShouldReadASetReset_WithItsSet()
    {
        const string id = "00000000-0000-4000-8000-000000000c06";
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id, targetId = "foreverpin-dev", release = "v1.0.0", actor = "test-admin", status = "succeeded",
            deployReason = "identity drifted", operation = new { kind = "reset", services = Array.Empty<string>(), set = "identity" },
            stopped = (object?)null, submittedAt = Instant(now.AddHours(-1)), completedAt = Instant(now.AddHours(-1).AddSeconds(30)),
        });

        var operation = (await DataAsync("/api/deployments/history")).GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == id).GetProperty("operation");
        (operation.GetProperty("kind").GetString(), operation.GetProperty("set").GetString()).Should().Be(("reset", "identity"));
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
    [InlineData("{\"operation\":\"stop\",\"set\":\"identity\"}", "Only a reset names a migration set")]
    [InlineData("{\"operation\":\"reset\",\"set\":\"Identity;rm\"}", "migration set's name")]
    [InlineData("{\"operation\":\"restart\"}", "Restart one service at a time")]
    [InlineData("{\"operation\":\"stop\",\"service\":\"Management;rm\"}", "Service")]
    [InlineData("{\"operation\":\"copy\"}", "A copy names the environment it copies from")]
    [InlineData("{\"operation\":\"copy\",\"from\":\"harvest-dev\",\"service\":\"management\"}", "never one service")]
    [InlineData("{\"operation\":\"copy\",\"from\":\"foreverpin-dev\"}", "never its own")]
    [InlineData("{\"operation\":\"copy\",\"from\":\"Harvest;rm\"}", "From")]
    [InlineData("{\"operation\":\"stop\",\"from\":\"harvest-dev\"}", "Only a copy names an environment to copy from")]
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

        foreach (var body in new object[] { new { operation = "stop" }, new { operation = "restart", service = "console" }, new { operation = "reset" },
                     new { operation = "reset", set = "console" },
                     new { operation = "run", service = "console", command = new[] { "dotnet", "Wheelhouse.Api.dll", "migrate" } } })
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

    /// <summary>Adds a Harvest product with its dev on ForeverPin dev's server and its prod on ForeverPin prod's, so each
    /// has a source on its own server to copy from; answers dev's server and ForeverPin's id.</summary>
    private async Task<(Guid Pilot, Guid ForeverPin)> AddHarvestAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var products = services.GetRequiredService<IProductsRepository>();
        var targets = services.GetRequiredService<ITargetsRepository>();
        var foreverPin = await products.GetBySlugAsync("foreverpin");
        var dev = await targets.GetBySlugAsync("foreverpin-dev");
        var prod = await targets.GetBySlugAsync("foreverpin-prod");
        var harvest = await products.CreateAsync(new ProductEntity
        {
            Id = Guid.NewGuid(), Slug = "harvest", Name = "Harvest", Description = "Scrapes and keeps listings.",
            Repository = "sulton-max/harvest", DefaultBranch = "main", WorkspaceId = foreverPin!.WorkspaceId,
        });
        await targets.CreateAsync(Target("harvest-dev", harvest.Id, dev!.ServerId, DeploymentEnvironment.Dev));
        await targets.CreateAsync(Target("harvest-prod", harvest.Id, prod!.ServerId, DeploymentEnvironment.Prod));
        return (dev.ServerId, foreverPin.Id);
    }

    private static TargetEntity Target(string slug, Guid product, Guid server, DeploymentEnvironment environment) => new()
    {
        Id = Guid.NewGuid(), Slug = slug, ProductId = product, ServerId = server, Environment = environment,
        Network = "platform", Root = "/srv/wheelhouse",
    };

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
