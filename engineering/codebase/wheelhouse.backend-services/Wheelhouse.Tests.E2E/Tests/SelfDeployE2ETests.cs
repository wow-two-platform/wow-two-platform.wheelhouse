using System.Globalization;
using System.Net;
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
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for Wheelhouse deploying itself (decision 64): once its product deploys with Wheelhouse, prod's inventory holds
/// both consoles on the control host. Dev takes automatic deploys once the owner turns them on; prod is promoted only by
/// the owner, with its ID typed and a reason, never by automation, never in a fleet and never while one runs. After the
/// swap, the new console reads its own rollout back from the host's journal.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class SelfDeployE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task SelfPromotion_ShouldTakeTheTypedIdAndAReason_AndBeAudited()
    {
        await SelfDeployingAsync();

        foreach (var (body, refusal) in new (object, string)[]
                 {
                     (new { target = "wheelhouse-prod", release = "wheelhouse-v0-5-21", reason = "v0.5 trial" },
                         "Type wheelhouse-prod to promote Wheelhouse prod."),
                     (new { target = "wheelhouse-prod", release = "wheelhouse-v0-5-21", confirm = "wheelhouse-prod" },
                         "Give a reason to promote Wheelhouse prod."),
                 })
        {
            var refused = await DeployAsync(body);
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await refused.Content.ReadAsStringAsync()).Should().Contain(refusal);
        }
        Fixture.Deployments.Starts.Should().BeEmpty("the guard refuses before the runner is asked");

        var accepted = await DeployAsync(new
        {
            target = "wheelhouse-prod", release = "wheelhouse-v0-5-21", confirm = "wheelhouse-prod", reason = "v0.5 trial",
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        Fixture.Deployments.Starts.Should().Equal(("wheelhouse-prod", "wheelhouse-v0-5-21", "test-admin"));
        (Fixture.Deployments.LastConfirm, Fixture.Deployments.LastReason).Should().Be(("wheelhouse-prod", "v0.5 trial"));

        var audit = (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "deployment.start").ToList();
        audit.Select(entry => entry.GetProperty("outcome").GetString()).Should().Contain(["succeeded", "failed"]);
        audit.First(entry => entry.GetProperty("outcome").GetString() == "succeeded").GetProperty("detail").GetString()
            .Should().Be("wheelhouse-v0-5-21, reason: v0.5 trial");
    }

    [Fact]
    public async Task SelfPromotion_ShouldWaitForARunningFleet()
    {
        await SelfDeployingAsync();
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IFleetRolloutsRepository>().AddAsync(new FleetRolloutEntity
            {
                Id = Guid.NewGuid(), Actor = "test-admin", Status = FleetRolloutStatus.Running,
                CreatedAt = Fixture.Host.Clock.GetUtcNow(),
            });

        var refused = await DeployAsync(new
        {
            target = "wheelhouse-prod", release = "wheelhouse-v0-5-21", confirm = "wheelhouse-prod", reason = "v0.5 trial",
        });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("A fleet rollout is running");

        // Its dev is not the console in use here: a running fleet never holds it back.
        (await DeployAsync(new { target = "wheelhouse-dev", release = "wheelhouse-v0-5-21" })).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Wheelhouse_ShouldNeverJoinAFleet()
    {
        await SelfDeployingAsync();
        SeedHistory(Fixture.Host.Clock.GetUtcNow(),
            ("00000000-0000-4000-8000-0000000000a1", "wheelhouse-dev", "v0.5.21", "succeeded", TimeSpan.FromHours(1)));

        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        var response = await client.PostAsJsonAsync("/api/deployments/fleets", new
        {
            items = new[] { new { target = "wheelhouse-prod", release = "wheelhouse-v0-5-21", confirm = "wheelhouse-prod" } },
            reason = "train",
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("runs Wheelhouse itself");
        Fixture.Deployments.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task Automation_ShouldDeployWheelhouseDevLikeAnyProduct_ButNeverAutoPromote()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "automation");
        // While its CI deploys it, the owner turns its dev deploys off first, so CI and prod never race for dev.
        (await client.PutAsJsonAsync("/api/deployments/automation/wheelhouse", new { autoDeployDev = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await SelfDeployingAsync();
        var described = (await DataAsync("/api/deployments/automation")).GetProperty("products").EnumerateArray()
            .Single(item => item.GetProperty("product").GetString() == "wheelhouse");
        (described.GetProperty("autoDeployDev").GetBoolean(), described.GetProperty("autoPromote").GetBoolean())
            .Should().Be((false, false));

        var promote = await client.PutAsJsonAsync("/api/deployments/automation/wheelhouse", new { autoPromote = true });
        promote.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await promote.Content.ReadAsStringAsync()).Should().Contain("never promotes itself");
        var on = await client.PutAsJsonAsync("/api/deployments/automation/wheelhouse", new { autoDeployDev = true });
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await on.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (data.GetProperty("autoDeployDev").GetBoolean(), data.GetProperty("autoPromote").GetBoolean()).Should().Be((true, false));

        // A new release reaches its dev through the same watcher and queue as any product's, never its prod.
        Fixture.Deployments.Latest.Add(new { product = "wheelhouse", ok = true,
            release = new { id = "wheelhouse-v0-5-21", release = "v0.5.21", publishedAt = "2026-10-01T10:00:00Z" } });
        await PassAsync();
        Fixture.Deployments.Latest.Clear();
        Fixture.Deployments.Latest.Add(new { product = "wheelhouse", ok = true,
            release = new { id = "wheelhouse-v0-5-22", release = "v0.5.22", publishedAt = "2026-10-02T10:00:00Z" } });
        (await PassAsync()).Submitted.Should().Be(1);
        Fixture.Deployments.Starts.Should().Equal(("wheelhouse-dev", "wheelhouse-v0-5-22", "automation"));
    }

    private async Task<Wheelhouse.Application.Deployments.Automation.Models.DeploymentAutomationPass> PassAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Wheelhouse.Application.Deployments.Automation.Services.DeploymentAutomationService>()
            .RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AnAutomaticDeployOfWheelhouseProd_ShouldBeRefused()
    {
        await SelfDeployingAsync();
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<WoW.Two.Sdk.Beta.Be.Mediator.ISender>().SendAsync(
            new Wheelhouse.Application.Deployments.DeploymentStartCommand("wheelhouse-prod", "wheelhouse-v0-5-21", "automation",
                Confirm: "wheelhouse-prod", Reason: "auto", Cause: DeploymentCause.AutoPromote));
        result.IsSuccess.Should().BeFalse();
        Fixture.Deployments.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheNewConsole_ShouldReadItsOwnRolloutBackFromTheHostJournal()
    {
        // The test host serves dev, so its own target is wheelhouse-dev; prod reads wheelhouse-prod the same way.
        await SelfDeployingAsync();
        var empty = await DataAsync("/api/self-deployment");
        (empty.GetProperty("environment").GetString(), empty.GetProperty("target").GetString()).Should().Be(("dev", "wheelhouse-dev"));
        empty.GetProperty("latest").ValueKind.Should().Be(JsonValueKind.Null);

        // The swap was submitted by the console this one replaced, before this one started.
        const string swap = "00000000-0000-4000-8000-0000000000b2";
        var booted = empty.GetProperty("bootedAt").GetDateTimeOffset();
        SeedHistory(booted,
            ("00000000-0000-4000-8000-0000000000b1", "wheelhouse-dev", "v0.5.20", "succeeded", TimeSpan.FromDays(1)),
            (swap, "wheelhouse-dev", "v0.5.21", "running", TimeSpan.FromMinutes(2)));
        // The console that started the swap is gone; the host's journal finished it. A status read brings that in.
        var reads = new List<string?>();
        Fixture.Deployments.Runner = new JournalGateway(Fixture.Deployments, reads, swap);

        var read = (await DataAsync("/api/self-deployment")).GetProperty("latest");
        reads.Should().Equal(swap);
        (read.GetProperty("id").GetString(), read.GetProperty("release").GetString(), read.GetProperty("status").GetString(),
            read.GetProperty("outcome").GetString(), read.GetProperty("previousRelease").GetString(),
            read.GetProperty("beforeBoot").GetBoolean(), read.GetProperty("reason").GetString())
            .Should().Be((swap, "v0.5.21", "succeeded", "succeeded", "v0.5.20", true, "v0.5 trial"));

        // A finished rollout is read from the records alone.
        await DataAsync("/api/self-deployment");
        reads.Should().HaveCount(1);
    }

    [Fact]
    public async Task TheReadBack_ShouldNeedSignIn()
    {
        (await AnonymousClient.GetAsync("/api/self-deployment")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Wheelhouse deploys itself: its product in deploy mode <c>wheelhouse</c>, both consoles on the control host.</summary>
    private async Task SelfDeployingAsync()
    {
        // Its public releases, as prod's inventory holds them, and its deploy mode switched to Wheelhouse.
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "product");
        (await client.PutAsJsonAsync("/api/products/wheelhouse", new
        {
            name = "Wheelhouse", description = "The portfolio's control plane.",
            repository = "wow-two-platform/wow-two-platform.wheelhouse", defaultBranch = "main", deploy = "wheelhouse",
            release = new
            {
                asset = "wheelhouse-release.tar.gz", workflow = "publish-docker-image.yml",
                images = new[] { new { service = "console", image = "ghcr.io/wow-two-platform/wow-two-platform.wheelhouse/console" } },
            },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var product = await services.GetRequiredService<IProductsRepository>().GetBySlugAsync("wheelhouse");
        var control = await services.GetRequiredService<IServersRepository>().CreateAsync(new ServerEntity
        {
            Id = Guid.NewGuid(), Slug = "control", Name = "Control host", Provider = VpsProvider.Ovhcloud,
            Host = "172.30.0.1", Region = "bhs", SshUser = "wheelhouse-deploy", SshPort = 22,
            Ingress = new ServerIngressValueObject(),
        });
        var targets = services.GetRequiredService<ITargetsRepository>();
        foreach (var (slug, environment) in new[] { ("wheelhouse-dev", DeploymentEnvironment.Dev), ("wheelhouse-prod", DeploymentEnvironment.Prod) })
            await targets.CreateAsync(new TargetEntity
            {
                Id = Guid.NewGuid(), Slug = slug, ProductId = product!.Id, ServerId = control.Id, Environment = environment,
                Network = "control", Root = "/srv/wheelhouse",
                Settings = [new TargetSettingValueObject { Service = "console", Path = "/srv/wheelhouse/config/console-" + environment.ToString().ToLowerInvariant() + ".json" }],
            });
    }

    private void SeedHistory(DateTimeOffset now, params (string Id, string Target, string Release, string Status, TimeSpan Ago)[] records)
    {
        foreach (var record in records)
            Fixture.Deployments.History.Add(new
            {
                id = record.Id, targetId = record.Target, bundleId = "wheelhouse-" + record.Release.Replace('.', '-'),
                release = record.Release, status = record.Status, kind = "release", product = "wheelhouse",
                actor = "test-admin", deployReason = "v0.5 trial",
                submittedAt = (now - record.Ago).ToString("O", CultureInfo.InvariantCulture),
                completedAt = record.Status == "running" ? null : (now - record.Ago).ToString("O", CultureInfo.InvariantCulture),
                publishedAt = (now - record.Ago - TimeSpan.FromHours(1)).ToString("O", CultureInfo.InvariantCulture),
            });
    }

    private Task<HttpResponseMessage> DeployAsync(object body)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        return client.PostAsJsonAsync("/api/deployments", body);
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    /// <summary>The host's journal: a status read of the running swap finds it finished, as the runner's status read
    /// refreshes the observation the history reads; every other read is the stub's.</summary>
    private sealed class JournalGateway(StubDeploymentGateway stub, List<string?> reads, string swap) : IDeploymentGateway
    {
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
        {
            if (resource == "status")
            {
                reads.Add(id);
                var index = stub.History.FindIndex(item => JsonSerializer.SerializeToElement(item).GetProperty("id").GetString() == swap);
                var finished = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(stub.History[index]))!;
                finished["status"] = "succeeded";
                finished["completedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                stub.History[index] = finished;
                return Task.FromResult(WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(finished)));
            }
            var runner = stub.Runner;
            stub.Runner = null;
            try
            {
                return stub.ReadAsync(resource, id, ct);
            }
            finally
            {
                stub.Runner = runner;
            }
        }

        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) => stub.CheckAsync(target, release, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, CancellationToken ct) => stub.StartAsync(target, release, actor, confirm, skipDevPass, reason, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) => stub.ReconcileAsync(target, job, actor, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) => stub.CommitsAsync(product, branch, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> ChangelogAsync(string product, string? from, string to, CancellationToken ct) => stub.ChangelogAsync(product, from, to, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) => stub.RequestBuildAsync(product, commit, ct);
        public Task<WoW.Two.Sdk.Beta.Be.Mediator.Result.AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) => stub.LogsAsync(target, service, tail, ct);
    }
}
