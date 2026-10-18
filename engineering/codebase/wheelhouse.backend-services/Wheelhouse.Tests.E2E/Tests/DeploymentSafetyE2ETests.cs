using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for what a prod deploy weighs before it goes: the rollback risk and migrations a release ready for prod
/// declares, a release's facts read on its own, and a reason required to skip the dev pass.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class DeploymentSafetyE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task Promotions_ShouldNameARelease_ThatCannotRollBackAutomatically_AndCarriesMigrations()
    {
        Fixture.Deployments.History.Add(Record("00000000-0000-4000-8000-000000000001", "foreverpin-prod", "v1.0.0", "foreverpin-gh-1",
            "2026-10-01T10:00:00+00:00", rollbackCompatible: true, migrations: null));
        Fixture.Deployments.History.Add(Record("00000000-0000-4000-8000-000000000002", "foreverpin-dev", "v1.1.0", "foreverpin-gh-2",
            "2026-10-02T10:00:00+00:00", rollbackCompatible: false, migrations: true));

        var offer = (await DataAsync("/api/deployments/promotions")).EnumerateArray().Single();
        offer.GetProperty("release").GetString().Should().Be("v1.1.0");
        offer.GetProperty("rollbackCompatible").GetBoolean().Should().BeFalse();
        offer.GetProperty("hasMigrations").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseFacts_ShouldReadOneBundle_AndRefuseAnythingButACatalogId()
    {
        (await AnonymousClient.GetAsync("/api/deployments/releases/foreverpin-gh-2/facts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient.GetAsync("/api/deployments/releases/foreverpin-gh-2/facts")).StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastRead.Should().Be(("facts", "foreverpin-gh-2"));
        (await AdminClient.GetAsync("/api/deployments/releases/Foreverpin;id/facts")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changelog_ShouldReadTheReleasesBetweenWhatProdRunsAndTheCandidate()
    {
        (await AnonymousClient.GetAsync("/api/deployments/products/foreverpin/changelog?to=v1.1.0"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var changelog = await DataAsync("/api/deployments/products/foreverpin/changelog?from=v1.0.0&to=v1.1.0");
        Fixture.Deployments.LastChangelog.Should().Be(("foreverpin", "v1.0.0", "v1.1.0"));
        changelog.GetProperty("releases")[0].GetProperty("body").GetString().Should().Be("- fix: login");
        (await DataAsync("/api/deployments/products/foreverpin/changelog?to=v1.1.0")).ValueKind.Should().Be(JsonValueKind.Object);
        Fixture.Deployments.LastChangelog.Should().Be(("foreverpin", (string?)null, "v1.1.0"), "a prod that runs nothing compares from nothing");
    }

    [Theory]
    [InlineData("/api/deployments/products/foreverpin/changelog")]
    [InlineData("/api/deployments/products/foreverpin/changelog?to=main")]
    [InlineData("/api/deployments/products/foreverpin/changelog?from=v1;id&to=v1.1.0")]
    [InlineData("/api/deployments/products/Foreverpin/changelog?to=v1.1.0")]
    public async Task Changelog_ShouldRefuseAnythingButReleaseTags(string path) =>
        (await AdminClient.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Detail_ShouldCarryTheSnapshotAProdRolloutTook_AndTheDeployReason()
    {
        const string id = "00000000-0000-4000-8000-000000000003";
        Fixture.Deployments.History.Add(new
        {
            id, targetId = "foreverpin-prod", bundleId = "foreverpin-gh-2", release = "v1.1.0", actor = "max", status = "succeeded",
            deployReason = "billing fix", submittedAt = "2026-10-03T10:00:00+00:00", completedAt = "2026-10-03T10:03:00+00:00",
        });
        Fixture.Deployments.Observations[id] = new
        {
            id, status = "succeeded",
            steps = new[] { new { name = "Snapshot data", status = "succeeded", detail = "1 database and 1 volume in /srv/wheelhouse/foreverpin-prod/snapshots/x" } },
            snapshot = new
            {
                path = "/srv/wheelhouse/foreverpin-prod/snapshots/x", createdAt = "2026-10-03T10:00:05+00:00",
                databases = new[] { new { service = "management", host = "postgres", port = 5432, database = "foreverpin_prod", file = "postgres-foreverpin_prod.dump", bytes = 2048 } },
                volumes = new[] { new { volume = "foreverpin-prod_keys", file = "foreverpin-prod_keys.tar.gz", bytes = 512 } },
                restore = new[] { "docker stop $(docker ps --quiet --filter label=com.docker.compose.project=foreverpin-prod)" },
            },
        };

        var detail = await DataAsync("/api/deployments/" + id + "/detail");
        detail.GetProperty("deployment").GetProperty("deployReason").GetString().Should().Be("billing fix");
        var snapshot = detail.GetProperty("snapshot");
        snapshot.GetProperty("path").GetString().Should().Be("/srv/wheelhouse/foreverpin-prod/snapshots/x");
        snapshot.GetProperty("databases")[0].GetProperty("database").GetString().Should().Be("foreverpin_prod");
        snapshot.GetProperty("volumes")[0].GetProperty("bytes").GetInt64().Should().Be(512);
        snapshot.GetProperty("restore").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task BuildFailures_ShouldListAFailingMainBuild_OfAKnownProduct_WithItsRun()
    {
        (await AnonymousClient.GetAsync("/api/deployments/issues/builds")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Fixture.Deployments.Builds.Add(new
        {
            product = "foreverpin", ok = true,
            failing = new { runId = 9, conclusion = "failure", commit = new string('b', 40), title = "feat: billing", at = "2026-10-18T09:05:00Z",
                url = "https://github.com/sulton-max/10x-venture-forever-pin/actions/runs/9" },
        });
        Fixture.Deployments.Builds.Add(new { product = "wheelhouse", ok = true, failing = (object?)null });
        Fixture.Deployments.Builds.Add(new { product = "gone", ok = true, failing = new { runId = 1, url = "https://github.com/o/gone/actions/runs/1" } });
        Fixture.Deployments.Builds.Add(new { product = "foreverpin-evil", ok = true, failing = new { runId = 2, url = "https://evil.example/run" } });

        var failure = (await DataAsync("/api/deployments/issues/builds")).EnumerateArray().Single();
        (failure.GetProperty("product").GetString(), failure.GetProperty("workspace").GetString(), failure.GetProperty("runId").GetInt64(),
                failure.GetProperty("title").GetString())
            .Should().Be(("foreverpin", "own-ventures", 9L, "feat: billing"));
        (await DataAsync("/api/deployments/issues/builds?workspace=ocharo")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Usage_ShouldReadOneProductsBuildCost_ForTheOwnerOnly()
    {
        (await AnonymousClient.GetAsync("/api/deployments/products/foreverpin/usage")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient.GetAsync("/api/deployments/products/foreverpin/usage")).StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastRead.Should().Be(("usage", "foreverpin"));
        (await AdminClient.GetAsync("/api/deployments/products/Foreverpin/usage")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static object Record(string id, string target, string release, string bundle, string submitted, bool rollbackCompatible,
        bool? migrations) => new
    {
        id, targetId = target, bundleId = bundle, release, actor = "max", status = "succeeded", submittedAt = submitted,
        completedAt = DateTimeOffset.Parse(submitted, System.Globalization.CultureInfo.InvariantCulture).AddMinutes(2)
            .ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        product = "foreverpin", kind = "release", rollbackCompatible, migrations, publishedAt = submitted,
    };

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
