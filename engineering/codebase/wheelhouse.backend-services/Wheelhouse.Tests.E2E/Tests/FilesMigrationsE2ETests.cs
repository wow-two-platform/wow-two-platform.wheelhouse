using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the files migration: the owner plans, pushes or verifies a product's legacy files into a Files host for one
/// dev target (a fake runner gateway, a fake Files host and the manifest read from a fake GitHub), and cancels the job.
/// The import token reaches the runner's payload only, and the operator key reaches only the Files import paths.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class FilesMigrationsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Repository = "sulton-max/10x-venture-forever-pin";
    private const string Manifest = "engineering/deployment/files-migration.yml";
    private const string FilesUrl = "https://files.ocharo.test/";
    private const string OperatorKey = "0123456789abcdef0123456789abcdef";
    private const string Image = "ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.files/files@sha256:aaaa";

    private const string ManifestYaml = """
        tenant: ocharo
        sources:
          - id: brand-media
            wave: 1
            kind: volume-folder
            volume: ocharo-media
            path: brand
            reference: brand-media
          - id: desk-preparation-media
            wave: 2
            kind: postgres-column
            database: desk
            table: preparations
            key: [id, version]
            content: media
            reference: desk-preparation-media
        """;

    [Fact]
    public async Task Plan_ShouldQueueTheJobWithTheSourcesAndTheImage_AndCallNothingOnFiles()
    {
        await PrepareAsync();
        var response = await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        Fixture.FilesHost.Requests.Should().BeEmpty("a plan calls nothing");
        var (target, actor, payload) = Fixture.Deployments.FilesMigrations.Should().ContainSingle().Which;
        (target, actor).Should().Be(("foreverpin-dev", "test-admin"));
        payload.GetProperty("step").GetString().Should().Be("plan");
        payload.GetProperty("image").GetString().Should().Be(Image);
        payload.GetProperty("files").GetString().Should().Be(FilesUrl);
        payload.GetProperty("import").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("token").ValueKind.Should().Be(JsonValueKind.Null);
        var sources = payload.GetProperty("sources").EnumerateArray().ToList();
        sources.Select(source => source.GetProperty("id").GetString()).Should().Equal("brand-media", "desk-preparation-media");
        sources[1].GetProperty("key").EnumerateArray().Select(key => key.GetString()).Should().Equal("id", "version");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("operation").GetProperty("kind")
            .GetString().Should().Be("files-migration");

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray().First(entry => entry.GetProperty("action").GetString() == "files-migration.plan");
        audit.GetProperty("subject").GetString().Should().Be("foreverpin-dev");
        audit.GetProperty("detail").GetString().Should().Contain("brand-media,desk-preparation-media");
    }

    [Fact]
    public async Task Push_ShouldOpenAnImport_WithTheOperatorKey_AndTheTokenOnlyInThePayload()
    {
        await PrepareAsync();
        var response = await MigrateAsync("foreverpin-dev",
            new { step = "push", wave = 2, filesTarget = "files-dev", filesUrl = FilesUrl });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, text);
        var request = Fixture.FilesHost.Requests.Should().ContainSingle().Which;
        (request.Method, request.Url).Should().Be(("POST", FilesUrl + "api/files/v1/imports"));
        request.Key.Should().Be("files_" + OperatorKey, "the header is the prefix and the stored value, exactly");
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("tenant").GetString().Should().Be("ocharo");
        body.RootElement.GetProperty("sources").EnumerateArray().Select(id => id.GetString()).Should().Equal("desk-preparation-media");

        var payload = Fixture.Deployments.FilesMigrations.Should().ContainSingle().Which.Payload;
        payload.GetProperty("step").GetString().Should().Be("push");
        payload.GetProperty("import").GetString().Should().Be(StubFilesHost.ImportId);
        payload.GetProperty("token").GetString().Should().Be(StubFilesHost.Token);
        payload.GetProperty("sources").GetArrayLength().Should().Be(1);

        var audit = await AdminClient.GetStringAsync("/api/audit");
        audit.Should().Contain("files-migration.push").And.Contain(StubFilesHost.ImportId);
        foreach (var secret in new[] { StubFilesHost.Token, OperatorKey })
        {
            audit.Should().NotContain(secret);
            text.Should().NotContain(secret, "the job carries no token or key");
        }
    }

    [Fact]
    public async Task Verify_ShouldMintATokenForTheImport_AndNeedsOne()
    {
        await PrepareAsync();
        (await MigrateAsync("foreverpin-dev", new { step = "verify", filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fixture.FilesHost.Requests.Should().BeEmpty();

        var response = await MigrateAsync("foreverpin-dev",
            new { step = "verify", sources = new[] { "brand-media" }, import = "imp-3", filesTarget = "files-dev", filesUrl = FilesUrl });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var request = Fixture.FilesHost.Requests.Should().ContainSingle().Which;
        (request.Url, request.Key).Should().Be((FilesUrl + "api/files/v1/imports/imp-3/tokens", "files_" + OperatorKey));
        var payload = Fixture.Deployments.FilesMigrations.Should().ContainSingle().Which.Payload;
        (payload.GetProperty("step").GetString(), payload.GetProperty("import").GetString(), payload.GetProperty("token").GetString())
            .Should().Be(("verify", "imp-3", StubFilesHost.Token));
        payload.GetProperty("sources").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Sources_ShouldBePickedByWaveAndId_AndNoneIsABadRequest()
    {
        await PrepareAsync();
        (await MigrateAsync("foreverpin-dev", new { step = "plan", wave = 3, filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MigrateAsync("foreverpin-dev", new { step = "plan", wave = 1, sources = new[] { "desk-preparation-media" }, filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "wave and ids narrow together");
        (await MigrateAsync("foreverpin-dev", new { step = "plan", wave = 0, filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseSet_ShouldReadEachRepositoryAtItsCommit_AndARepositoryWithoutTheFileAddsNothing()
    {
        await PrepareAsync(release: false);
        var main = Fixture.GitHub.Push(Repository, Manifest, ManifestYaml);
        var studio = Fixture.GitHub.Push("owner/foreverpin-studio", Manifest,
            ManifestYaml.Replace("brand-media", "studio-media").Replace("desk-preparation-media", "studio-other"));
        var bare = Fixture.GitHub.Push("owner/foreverpin-bare", "README.md", "hi");
        // The heads move on; the release stays at the commits it was built from.
        Fixture.GitHub.Push(Repository, Manifest, ManifestYaml.Replace("brand-media", "head-only"));
        Fixture.Deployments.History.Add(Succeeded("foreverpin-dev", components: new object[]
        {
            new { repository = Repository, sourceCommit = main }, new { repository = "owner/foreverpin-studio", sourceCommit = studio },
            new { repository = "owner/foreverpin-bare", sourceCommit = bare },
        }));

        var response = await MigrateAsync("foreverpin-dev", new { step = "plan", wave = 1, filesTarget = "files-dev", filesUrl = FilesUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        Fixture.Deployments.FilesMigrations.Single().Payload.GetProperty("sources").EnumerateArray()
            .Select(source => source.GetProperty("id").GetString()).Should().Equal("brand-media", "studio-media");
    }

    [Fact]
    public async Task Prod_AndWheelhouseItself_ShouldBeRefused()
    {
        await PrepareAsync();
        var prod = await MigrateAsync("foreverpin-prod", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl });
        prod.StatusCode.Should().Be(HttpStatusCode.Conflict);

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

        var self = await MigrateAsync("wheelhouse-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl });
        self.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await self.Content.ReadAsStringAsync()).Should().Contain("runs Wheelhouse itself");
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
        (await MigrateAsync("nowhere-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MissingManifest_ShouldBeAConflict()
    {
        await PrepareAsync(manifest: false);
        var response = await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no migration manifest");
    }

    [Theory]
    [InlineData("tenant: ocharo\nsources:\n  - id: a\n    kind: volume-folder\n    volume: v\n    path: p\n", "reference")]
    [InlineData("tenant: ocharo\nsources:\n  - id: a\n    kind: mystery\n", "kind")]
    [InlineData("tenant: ocharo\nsources:\n  - id: a\n    wave: 0\n    kind: volume-folder\n    volume: v\n    path: p\n    reference: r\n", "wave")]
    public async Task InvalidManifest_ShouldBeAConflictNamingTheField(string yaml, string field)
    {
        await PrepareAsync(release: false);
        Fixture.Deployments.History.Add(Succeeded("foreverpin-dev", commit: Fixture.GitHub.Push(Repository, Manifest, yaml)));
        var response = await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field);
    }

    [Theory]
    [InlineData("http://files.ocharo.test/")]
    [InlineData("https://files.ocharo.test")]
    [InlineData("https://elsewhere.example/")]
    [InlineData("not a url/")]
    public async Task BadUrl_ShouldBeABadRequest(string url)
    {
        await PrepareAsync();
        (await MigrateAsync("foreverpin-dev", new { step = "push", filesTarget = "files-dev", filesUrl = url }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fixture.FilesHost.Requests.Should().BeEmpty();
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
    }

    [Fact]
    public async Task Url_ShouldAcceptTheAppHostAndItsAliases()
    {
        await PrepareAsync();
        foreach (var url in new[] { "https://files.dev.ocharo.test/", "https://FILES.ocharo.test/" })
            (await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = url }))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Theory]
    [InlineData("nowhere-dev")]
    [InlineData("foreverpin-dev")]
    [InlineData("foreverpin-prod")]
    public async Task UnknownFilesTarget_ShouldBeABadRequest(string filesTarget)
    {
        await PrepareAsync();
        (await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget, filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingOperatorKey_ShouldBeAConflictNamingIt_AndAFilesErrorIsABadGatewayWithoutItsBody()
    {
        await PrepareAsync(key: false);
        var missing = await MigrateAsync("foreverpin-dev", new { step = "push", filesTarget = "files-dev", filesUrl = FilesUrl });
        missing.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("Files:Operator:ApiKey");
        (await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted, "a plan needs no key");

        await StoreKeyAsync();
        Fixture.FilesHost.Failure = HttpStatusCode.InternalServerError;
        var bad = await MigrateAsync("foreverpin-dev", new { step = "push", filesTarget = "files-dev", filesUrl = FilesUrl });
        bad.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var text = await bad.Content.ReadAsStringAsync();
        text.Should().Contain("500").And.NotContain("secret-body-from-files").And.NotContain(OperatorKey);
        Fixture.Deployments.FilesMigrations.Should().ContainSingle("only the plan reached the runner");
    }

    [Fact]
    public async Task Migration_ShouldBeTheOwnersAlone()
    {
        await PrepareAsync();
        var body = new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl };
        var anonymous = AnonymousClient;
        anonymous.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        (await anonymous.PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/files-migrations", body)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var created = await admin.PostAsJsonAsync("/api/integration-keys", new { name = "Claude", scopes = new[] { "deployments:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var key = AnonymousClient;
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        key.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        (await key.PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/files-migrations", body)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await AdminClient.PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/files-migrations", body)).StatusCode
            .Should().NotBe(HttpStatusCode.Accepted, "a write without its action header is refused");
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_ShouldStopAFilesMigrationJob_AndRefuseAnyOther()
    {
        await PrepareAsync();
        var job = Guid.NewGuid();
        var other = Guid.NewGuid();
        Fixture.Deployments.History.Add(new
        {
            id = job.ToString(), targetId = "foreverpin-dev", environment = "dev", status = "running", actor = "test-admin",
            submittedAt = "2026-11-08T10:00:00+00:00", operation = new { kind = "files-migration", services = Array.Empty<string>() },
        });
        Fixture.Deployments.History.Add(new
        {
            id = other.ToString(), targetId = "foreverpin-dev", environment = "dev", status = "running", actor = "test-admin",
            submittedAt = "2026-11-08T10:00:00+00:00", operation = new { kind = "stop", services = Array.Empty<string>() },
        });

        (await CancelAsync("foreverpin-dev", other)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CancelAsync("foreverpin-dev", Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CancelAsync("nowhere-dev", job)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Fixture.Deployments.Cancels.Should().BeEmpty();

        var cancelled = await CancelAsync("foreverpin-dev", job);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        Fixture.Deployments.Cancels.Should().Equal(("foreverpin-dev", job.ToString("D"), "test-admin"));
        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "files-migration.cancel" && entry.GetProperty("outcome").GetString() == "succeeded");
        audit.GetProperty("subject").GetString().Should().Be("foreverpin-dev");

        var anonymous = AnonymousClient;
        anonymous.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        (await anonymous.PostAsync($"/api/deployments/targets/foreverpin-dev/controls/{job}/cancel", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Manifest_ShouldListTheSourcesOfEveryRepository_AndTheFilesTargetsWithTheirAliases()
    {
        await PrepareAsync(release: false);
        var main = Fixture.GitHub.Push(Repository, Manifest, ManifestYaml);
        var studio = Fixture.GitHub.Push("owner/foreverpin-studio", Manifest,
            ManifestYaml.Replace("brand-media", "studio-media").Replace("desk-preparation-media", "studio-other").Replace("wave: 2", "wave: 3"));
        var bare = Fixture.GitHub.Push("owner/foreverpin-bare", "README.md", "hi");
        Fixture.Deployments.History.Add(Succeeded("foreverpin-dev", components: new object[]
        {
            new { repository = Repository, sourceCommit = main }, new { repository = "owner/foreverpin-studio", sourceCommit = studio },
            new { repository = "owner/foreverpin-bare", sourceCommit = bare },
        }));

        var response = await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/files-migrations/manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("tenant").GetString().Should().Be("ocharo");
        data.GetProperty("sources").EnumerateArray()
            .Select(source => (source.GetProperty("id").GetString(), source.GetProperty("kind").GetString(),
                source.GetProperty("wave").GetInt32(), source.GetProperty("repository").GetString()))
            .Should().Equal(
                ("brand-media", "volume-folder", 1, Repository), ("desk-preparation-media", "postgres-column", 2, Repository),
                ("studio-media", "volume-folder", 1, "owner/foreverpin-studio"), ("studio-other", "postgres-column", 3, "owner/foreverpin-studio"));
        var files = data.GetProperty("filesTargets").EnumerateArray().Should().ContainSingle("only files-dev is a dev files target").Which;
        files.GetProperty("target").GetString().Should().Be("files-dev");
        files.GetProperty("urls").EnumerateArray().Select(url => url.GetString())
            .Should().Equal("https://files.dev.ocharo.test/", "https://files.ocharo.test/");
        Fixture.FilesHost.Requests.Should().BeEmpty("a read calls nothing");
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
    }

    [Fact]
    public async Task Manifest_ShouldRefuseProdSelfAndAMissingManifest_AndTheOwnerAloneReadsIt()
    {
        await PrepareAsync(manifest: false);
        var none = await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/files-migrations/manifest");
        none.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await none.Content.ReadAsStringAsync()).Should().Contain("no migration manifest");
        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-prod/files-migrations/manifest")).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
        (await AdminClient.GetAsync("/api/deployments/targets/nowhere-dev/files-migrations/manifest")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

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

        var self = await AdminClient.GetAsync("/api/deployments/targets/wheelhouse-dev/files-migrations/manifest");
        self.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await self.Content.ReadAsStringAsync()).Should().Contain("runs Wheelhouse itself");

        (await AnonymousClient.GetAsync("/api/deployments/targets/foreverpin-dev/files-migrations/manifest")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var created = await admin.PostAsJsonAsync("/api/integration-keys", new { name = "Claude", scopes = new[] { "deployments:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var key = AnonymousClient;
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        (await key.GetAsync("/api/deployments/targets/foreverpin-dev/files-migrations/manifest")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("push")]
    [InlineData("verify")]
    public async Task PushAndVerify_ShouldCheckTheFilesHostFirst_AndTakeNoKeyToIt(string step)
    {
        await PrepareAsync();
        var response = await MigrateAsync("foreverpin-dev",
            new { step, import = step == "verify" ? "imp-3" : null, filesTarget = "files-dev", filesUrl = FilesUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        Fixture.FilesHost.HealthChecks.Should().Equal((FilesUrl + "health", false));
        Fixture.FilesHost.Requests.Should().ContainSingle().Which.Url.Should().StartWith(FilesUrl + "api/files/v1/imports");
    }

    [Theory]
    [InlineData("push", HttpStatusCode.ServiceUnavailable, false, "503")]
    [InlineData("verify", HttpStatusCode.InternalServerError, false, "500")]
    [InlineData("push", HttpStatusCode.OK, true, "timeout")]
    public async Task UnhealthyFilesHost_ShouldBeAConflict_BeforeAnythingIsOpenedOrSubmitted(
        string step, HttpStatusCode status, bool timesOut, string detail)
    {
        await PrepareAsync();
        Fixture.FilesHost.HealthStatus = status;
        Fixture.FilesHost.HealthTimesOut = timesOut;

        var response = await MigrateAsync("foreverpin-dev",
            new { step, import = step == "verify" ? "imp-3" : null, filesTarget = "files-dev", filesUrl = FilesUrl });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain($"Files at files.ocharo.test is not healthy ({detail})").And.NotContain("secret-body-from-files");
        Fixture.FilesHost.Requests.Should().BeEmpty("no import opens and no token is minted");
        Fixture.Deployments.FilesMigrations.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_ShouldSkipTheHealthCheck_AndRunAgainstAnUnhealthyFilesHost()
    {
        await PrepareAsync();
        Fixture.FilesHost.HealthStatus = HttpStatusCode.ServiceUnavailable;
        (await MigrateAsync("foreverpin-dev", new { step = "plan", filesTarget = "files-dev", filesUrl = FilesUrl }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.FilesHost.HealthChecks.Should().BeEmpty();
        Fixture.Deployments.FilesMigrations.Should().ContainSingle();
    }

    [Fact]
    public async Task History_ShouldCarryTheMigrationsStepSourcesAndResults_AndReadACancelledJobAsCancelled()
    {
        await PrepareAsync();
        const string done = "00000000-0000-4000-8000-000000000f01";
        const string stopped = "00000000-0000-4000-8000-000000000f02";
        const string old = "00000000-0000-4000-8000-000000000f03";
        var now = Fixture.Host.Clock.GetUtcNow();
        object Job(string id, string status, object? migration, int hours) => new
        {
            id, targetId = "foreverpin-dev", environment = "dev", release = "v1.0.0", actor = "test-admin", status,
            operation = new { kind = "files-migration", step = "push", sources = new[] { "brand-media", "desk-preparation-media" } },
            filesMigration = migration, submittedAt = Instant(now.AddHours(-hours)), completedAt = Instant(now.AddHours(-hours).AddMinutes(2)),
        };
        var results = new
        {
            import = "imp-7",
            results = new Dictionary<string, object> { ["brand-media"] = new { files = 12, bytes = 3400, skipped = 2, failed = 0 } },
        };
        Fixture.Deployments.History.Add(Job(done, "succeeded", results, 3));
        Fixture.Deployments.History.Add(Job(stopped, "cancelled", results, 2));
        Fixture.Deployments.History.Add(Job(old, "succeeded", null, 1));
        Fixture.Deployments.Observations[stopped] = new
        {
            status = "cancelled",
            filesMigration = new { import = "imp-7", results = new Dictionary<string, object>
            {
                ["brand-media"] = new { files = 12, bytes = 3400, skipped = 2, failed = 0 },
                ["desk-preparation-media"] = new { files = 5, bytes = 90, skipped = 0, failed = 1 },
            } },
        };

        var items = (await ReadAsync("/api/deployments/history?notApplied=true")).GetProperty("items").EnumerateArray().ToList();
        var first = items.Single(item => item.GetProperty("id").GetString() == done);
        first.GetProperty("operation").GetProperty("step").GetString().Should().Be("push");
        first.GetProperty("operation").GetProperty("sources").EnumerateArray().Select(id => id.GetString())
            .Should().Equal("brand-media", "desk-preparation-media");
        var kept = first.GetProperty("filesMigration");
        kept.GetProperty("import").GetString().Should().Be("imp-7");
        var brand = kept.GetProperty("results").GetProperty("brand-media");
        (brand.GetProperty("files").GetInt64(), brand.GetProperty("bytes").GetInt64(), brand.GetProperty("skipped").GetInt64(), brand.GetProperty("failed").GetInt64())
            .Should().Be((12, 3400, 2, 0));

        var cancelled = items.Single(item => item.GetProperty("id").GetString() == stopped);
        (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("outcome").GetString(), cancelled.GetProperty("state").GetString(),
            cancelled.GetProperty("substate").GetString(), cancelled.GetProperty("attention").GetString())
            .Should().Be(("cancelled", "cancelled", "not_applied", "cancelled", "none"));
        items.Single(item => item.GetProperty("id").GetString() == old).GetProperty("filesMigration").ValueKind
            .Should().Be(JsonValueKind.Null, "a record without the field reads as before");

        var detail = (await ReadAsync($"/api/deployments/{stopped}/detail")).GetProperty("deployment");
        detail.GetProperty("outcome").GetString().Should().Be("cancelled");
        detail.GetProperty("filesMigration").GetProperty("results").EnumerateObject().Select(source => source.Name)
            .Should().Equal("brand-media", "desk-preparation-media");
        detail.GetProperty("filesMigration").GetProperty("results").GetProperty("desk-preparation-media").GetProperty("failed").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task History_ShouldReadAFailedMigrationAsNotApplied_AndKeepItsOperationWithoutServices()
    {
        await PrepareAsync();
        const string id = "00000000-0000-4000-8000-000000000f04";
        var now = Fixture.Host.Clock.GetUtcNow();
        Fixture.Deployments.History.Add(new
        {
            id, targetId = "foreverpin-dev", environment = "dev", release = "v1.0.0", actor = "test-admin", status = "failed",
            reason = "the mover failed on brand-media (exit 2)",
            operation = new { kind = "files-migration", step = "verify", sources = new[] { "brand-media" } },
            submittedAt = Instant(now.AddHours(-1)), completedAt = Instant(now.AddHours(-1).AddMinutes(1)),
        });

        var item = (await ReadAsync("/api/deployments/history?notApplied=true")).GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == id);
        (item.GetProperty("outcome").GetString(), item.GetProperty("state").GetString(), item.GetProperty("operation").GetProperty("services").GetArrayLength())
            .Should().Be(("failed", "not_applied", 0));
    }

    private static string Instant(DateTimeOffset at) => at.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<JsonElement> ReadAsync(string url)
    {
        var response = await AdminClient.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task PrepareAsync(bool manifest = true, bool key = true, bool release = true)
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var products = services.GetRequiredService<IProductsRepository>();
            var foreverPin = await products.GetBySlugAsync("foreverpin");
            var files = await products.CreateAsync(new ProductEntity
            {
                Id = Guid.NewGuid(), Slug = "files", Name = "Files", Description = "Shared file storage.",
                Repository = "wow-two-sdk-beta/wow-two-sdk-beta.files", DefaultBranch = "main", WorkspaceId = foreverPin!.WorkspaceId,
            });
            var pilot = (await services.GetRequiredService<IServersRepository>().GetBySlugAsync("pilot-host"))!;
            await services.GetRequiredService<ITargetsRepository>().CreateAsync(new TargetEntity
            {
                Id = Guid.NewGuid(), Slug = "files-dev", ProductId = files.Id, ServerId = pilot.Id, Environment = DeploymentEnvironment.Dev,
                Network = "platform", Root = "/srv/wheelhouse",
                Sites = [new SiteHostValueObject { Site = "app", Host = "files.dev.ocharo.test", Aliases = ["files.ocharo.test"] }],
            });
        }

        Fixture.Deployments.History.Add(Succeeded("files-dev", services: FilesServices()));
        if (release)
        {
            var commit = manifest ? Fixture.GitHub.Push(Repository, Manifest, ManifestYaml) : Fixture.GitHub.Push(Repository, "README.md", "hi");
            Fixture.Deployments.History.Add(Succeeded("foreverpin-dev", commit: commit));
            Fixture.Deployments.History.Add(Succeeded("foreverpin-prod", commit: commit));
        }
        if (key)
            await StoreKeyAsync();
    }

    private async Task StoreKeyAsync()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "settings");
        var saved = await client.PutAsJsonAsync("/api/deployments/targets/files-dev/settings",
            new { values = new[] { new { key = "Files:Operator:ApiKey", value = OperatorKey, secret = true } } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static object FilesServices() => new Dictionary<string, object> { ["files"] = new { image = Image, version = "0.1.0" } };

    private static object Succeeded(string target, string? commit = null, object? services = null, object[]? components = null) => new
    {
        id = Guid.NewGuid().ToString(), targetId = target, environment = target.EndsWith("prod", StringComparison.Ordinal) ? "prod" : "dev",
        bundleId = target + "-bundle", release = "v1.0.0", actor = "test-admin", status = "succeeded", sourceCommit = commit,
        submittedAt = "2026-11-08T09:00:00+00:00", services, components,
    };

    private Task<HttpResponseMessage> MigrateAsync(string target, object body) =>
        ActionClient().PostAsJsonAsync($"/api/deployments/targets/{target}/files-migrations", body);

    private Task<HttpResponseMessage> CancelAsync(string target, Guid job) =>
        ActionClient().PostAsync($"/api/deployments/targets/{target}/controls/{job}/cancel", null);

    private HttpClient ActionClient()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "control");
        return client;
    }
}
