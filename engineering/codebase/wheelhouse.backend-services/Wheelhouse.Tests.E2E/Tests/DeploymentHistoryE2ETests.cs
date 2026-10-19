using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Workspaces.Entities;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the deployment history the Deployments page reads: the runner's records joined with the inventory, then
/// filtered by entity (workspace, product, <c>product/service</c>, as a union), status and range; cursor paging; per-day
/// activity in the owner's time zone; measures beside the prior range; live rollouts; releases ready for prod; the
/// targets that need attention; and one deployment in full.
/// </summary>
/// <remarks>
/// The records: ForeverPin (own-ventures) dev and prod, Ocharo's shop (ocharo) dev only, and a removed target.
/// <list type="number">
/// <item>fp-dev v1.0.0 succeeded 10-01 — first rollout there, so every service is new.</item>
/// <item>fp-prod v1.0.0 succeeded 10-02.</item>
/// <item>fp-dev v1.1.0 failed 10-03 23:30 UTC (10-04 in Tashkent) after it changed containers — management changed.</item>
/// <item>ocharo-shop-dev v0.3.0 rolled back 10-04.</item>
/// <item>fp-dev v1.1.0 succeeded with a warning 10-05 — management changed against #1.</item>
/// <item>fp-prod v1.1.0 refused 10-05 — it changed nothing, so lists show it only when asked to.</item>
/// <item>fp-dev v1.2.0 running 10-06 — web changed.</item>
/// <item>gone-dev, an unknown bundle on a removed target, 09-01.</item>
/// </list>
/// </remarks>
[Collection(WheelhouseCollection.Name)]
public sealed class DeploymentHistoryE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string FirstDev = "00000000-0000-4000-8000-000000000001";
    private const string FirstProd = "00000000-0000-4000-8000-000000000002";
    private const string FailedDev = "00000000-0000-4000-8000-000000000003";
    private const string RolledBack = "00000000-0000-4000-8000-000000000004";
    private const string SecondDev = "00000000-0000-4000-8000-000000000005";
    private const string RefusedProd = "00000000-0000-4000-8000-000000000006";
    private const string Running = "00000000-0000-4000-8000-000000000007";
    private const string Removed = "00000000-0000-4000-8000-000000000008";

    private static readonly string ManagementA = "ghcr.io/o/fp/management@sha256:" + new string('a', 64);
    private static readonly string ManagementB = "ghcr.io/o/fp/management@sha256:" + new string('b', 64);
    private static readonly string WebA = "ghcr.io/o/fp/web@sha256:" + new string('c', 64);
    private static readonly string WebB = "ghcr.io/o/fp/web@sha256:" + new string('d', 64);

    [Theory]
    [InlineData("/api/deployments/history")]
    [InlineData("/api/deployments/history/activity")]
    [InlineData("/api/deployments/history/stats")]
    [InlineData("/api/deployments/running")]
    [InlineData("/api/deployments/promotions")]
    [InlineData("/api/deployments/issues")]
    [InlineData("/api/deployments/" + FirstDev + "/detail")]
    public async Task Reads_RequireTheOwner(string path)
    {
        await SeedAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnonymousClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AdminClient.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task History_ListsNewestFirstWithTheInventoryJoined()
    {
        await SeedAsync();
        Ids(await DataAsync("/api/deployments/history")).Should().Equal(Running, SecondDev, RolledBack, FailedDev, FirstProd, FirstDev, Removed);
        var page = await DataAsync("/api/deployments/history?notApplied=true");
        Ids(page).Should().Equal(Running, RefusedProd, SecondDev, RolledBack, FailedDev, FirstProd, FirstDev, Removed);
        page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

        var second = Item(page, SecondDev);
        (second.GetProperty("product").GetString(), second.GetProperty("workspace").GetString(),
                second.GetProperty("environment").GetString(), second.GetProperty("outcome").GetString())
            .Should().Be(("foreverpin", "own-ventures", "dev", "succeeded"));
        second.GetProperty("changedServices").EnumerateArray().Select(item => item.GetString()).Should().Equal("management");
        second.GetProperty("durationSeconds").GetDouble().Should().Be(150);
        second.GetProperty("warnings").GetArrayLength().Should().Be(1);

        Item(page, FirstDev).GetProperty("changedServices").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("management", "web");
        Item(page, Running).GetProperty("currentStep").GetProperty("name").GetString().Should().Be("Pull images");
        Item(page, Running).GetProperty("outcome").GetString().Should().Be("pending");
        Item(page, RolledBack).GetProperty("outcome").GetString().Should().Be("failed");
        Item(page, RefusedProd).GetProperty("outcome").GetString().Should().Be("refused");
        States(page).Should().Equal(("in_progress", "checking", "info"), ("not_applied", "refused", "none"),
            ("degraded", "at_deploy", "look"), ("rolled_back", "automatic", "look"), ("failed", "rollback_not_allowed", "act"),
            ("succeeded", null, "none"), ("succeeded", null, "none"), ("succeeded", null, "none"));
        Item(page, SecondDev).GetProperty("cause").GetProperty("origin").GetString().Should().Be("Probe sites");

        var removed = Item(page, Removed);
        removed.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        // A removed target keeps the environment the runner recorded at submission.
        removed.GetProperty("environment").GetString().Should().Be("dev");
        removed.GetProperty("changedServices").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("workspace=ocharo", new[] { RolledBack })]
    [InlineData("product=foreverpin", new[] { Running, SecondDev, FailedDev, FirstProd, FirstDev })]
    [InlineData("product=foreverpin&notApplied=true", new[] { Running, RefusedProd, SecondDev, FailedDev, FirstProd, FirstDev })]
    [InlineData("service=foreverpin/web", new[] { Running, FirstProd, FirstDev })]
    [InlineData("service=foreverpin/management", new[] { SecondDev, FailedDev, FirstProd, FirstDev })]
    [InlineData("service=foreverpin/web&workspace=ocharo", new[] { Running, RolledBack, FirstProd, FirstDev })]
    [InlineData("workspace=own-ventures&product=ocharo-shop", new[] { Running, SecondDev, RolledBack, FailedDev, FirstProd, FirstDev })]
    [InlineData("status=failed", new[] { RolledBack, FailedDev })]
    [InlineData("status=succeeded", new[] { FirstProd, FirstDev, Removed })]
    [InlineData("status=needs-action", new[] { FailedDev })]
    [InlineData("status=look", new[] { SecondDev, RolledBack })]
    [InlineData("status=not-applied", new[] { RefusedProd })]
    [InlineData("status=refused", new[] { RefusedProd })]
    [InlineData("status=failed&product=foreverpin", new[] { FailedDev })]
    [InlineData("from=2026-10-02T00:00:00Z&to=2026-10-05T00:00:00Z", new[] { RolledBack, FailedDev, FirstProd })]
    [InlineData("product=foreverpin&status=succeeded&from=2026-10-02T00:00:00Z", new[] { FirstProd })]
    [InlineData("workspace=nobody", new string[0])]
    public async Task History_FiltersByEntityStatusAndRange(string query, string[] expected)
    {
        await SeedAsync();
        Ids(await DataAsync("/api/deployments/history?" + query)).Should().Equal(expected);
    }

    [Fact]
    public async Task History_PagesWithACursorThatSurvivesANewSubmission()
    {
        await SeedAsync();
        var seen = new List<string?>();
        var first = await DataAsync("/api/deployments/history?limit=3");
        seen.AddRange(Ids(first));
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        // A submission landing between pages never shifts a later page.
        Fixture.Deployments.History.Add(Record("00000000-0000-4000-8000-000000000099", "foreverpin-dev", "2026-10-07T10:00:00+00:00",
            "queued", "v1.2.1", "foreverpin-gh-4", WebB));
        while (cursor is not null)
        {
            var page = await DataAsync("/api/deployments/history?limit=3&cursor=" + Uri.EscapeDataString(cursor));
            seen.AddRange(Ids(page));
            cursor = page.GetProperty("nextCursor").GetString();
        }

        seen.Should().Equal(Running, SecondDev, RolledBack, FailedDev, FirstProd, FirstDev, Removed);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("cursor=not-a-cursor")]
    [InlineData("workspace=Ocharo")]
    [InlineData("product=../etc")]
    [InlineData("service=foreverpin")]
    [InlineData("service=foreverpin/web/extra")]
    [InlineData("status=broken")]
    [InlineData("from=2026-10-05T00:00:00Z&to=2026-10-01T00:00:00Z")]
    [InlineData("from=yesterday")]
    public async Task History_RefusesAMalformedFilter(string query)
    {
        await SeedAsync();
        (await AdminClient.GetAsync("/api/deployments/history?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Activity_CountsFinishedRolloutsPerLocalDay()
    {
        await SeedAsync();
        const string range = "from=2026-09-28T00:00:00Z&to=2026-10-08T00:00:00Z";
        var tashkent = await DataAsync("/api/deployments/history/activity?" + range + "&tz=Asia/Tashkent");
        tashkent.GetProperty("from").GetString().Should().Be("2026-09-28");
        tashkent.GetProperty("to").GetString().Should().Be("2026-10-08");
        Days(tashkent).Should().Equal(("2026-10-01", 1, 0), ("2026-10-02", 1, 0), ("2026-10-04", 0, 2), ("2026-10-05", 1, 0));
        // The same finished rollouts name the busiest products, most first.
        Products(tashkent).Should().Equal(("foreverpin", 3, 1), ("ocharo-shop", 0, 1));

        // The failure at 23:30 UTC falls on the 3rd in UTC; refused and running submissions colour no day.
        var utc = await DataAsync("/api/deployments/history/activity?" + range + "&tz=UTC");
        Days(utc).Should().Equal(("2026-10-01", 1, 0), ("2026-10-02", 1, 0), ("2026-10-03", 0, 1), ("2026-10-04", 0, 1),
            ("2026-10-05", 1, 0));

        var failures = await DataAsync("/api/deployments/history/activity?" + range + "&tz=UTC&status=failed&product=foreverpin");
        Days(failures).Should().Equal(("2026-10-03", 0, 1));
        Products(failures).Should().Equal(("foreverpin", 0, 1));

        // Each day by the attention its applied rollouts need; the refusal counts only when asked for and never colours.
        Attention(utc).Should().Equal(("2026-10-01", 1, 0, 0, 0), ("2026-10-02", 1, 0, 0, 0), ("2026-10-03", 0, 0, 1, 0),
            ("2026-10-04", 0, 1, 0, 0), ("2026-10-05", 0, 1, 0, 0));
        var all = await DataAsync("/api/deployments/history/activity?" + range + "&tz=UTC&notApplied=true");
        Attention(all).Last().Should().Be(("2026-10-05", 0, 1, 0, 1));
        Attention(await DataAsync("/api/deployments/history/activity?" + range + "&tz=UTC&status=needs-action"))
            .Should().Equal(("2026-10-03", 0, 0, 1, 0));
    }

    [Fact]
    public async Task Latest_KeepsEachTargetsLastDeployOfALocalDayWhileTheStatsCountEveryAttempt()
    {
        await SeedAsync();
        // fp-dev on 10-07: four failures, then a fix; a refusal on prod; then a failure at 21:00 UTC, already 10-08 in
        // Tashkent.
        string[] failures = ["00000000-0000-4000-8000-0000000000a1", "00000000-0000-4000-8000-0000000000a2",
            "00000000-0000-4000-8000-0000000000a3", "00000000-0000-4000-8000-0000000000a4"];
        const string fixedDev = "00000000-0000-4000-8000-0000000000a5";
        const string refused = "00000000-0000-4000-8000-0000000000a6";
        const string late = "00000000-0000-4000-8000-0000000000a7";
        var history = Fixture.Deployments.History;
        for (var index = 0; index < failures.Length; index++)
            history.Add(Record(failures[index], "foreverpin-dev", $"2026-10-07T{8 + index:00}:00:00+00:00", "failed", "v1.2.1",
                "foreverpin-gh-4", WebB, management: ManagementB, managementVersion: "1.1.0", mutation: true));
        history.Add(Record(fixedDev, "foreverpin-dev", "2026-10-07T13:00:00+00:00", "succeeded", "v1.2.2", "foreverpin-gh-5",
            WebB, management: ManagementB, managementVersion: "1.1.0"));
        history.Add(Record(refused, "foreverpin-prod", "2026-10-07T14:00:00+00:00", "rejected", "v1.2.2", "foreverpin-gh-5",
            WebB, management: ManagementB, managementVersion: "1.1.0"));
        history.Add(Record(late, "foreverpin-dev", "2026-10-07T21:00:00+00:00", "failed", "v1.2.3", "foreverpin-gh-6",
            WebA, management: ManagementB, managementVersion: "1.1.0", mutation: true));
        const string range = "from=2026-10-07T00:00:00Z&to=2026-10-08T00:00:00Z";

        // Every attempt, the refusal included, until the view keeps each day's latest.
        Ids(await DataAsync("/api/deployments/history?notApplied=true&" + range)).Should()
            .Equal([late, refused, fixedDev, .. Enumerable.Reverse(failures)]);
        // In UTC the late failure is fp-dev's last deploy of the 7th: nothing fixed it, so it shows.
        Ids(await DataAsync("/api/deployments/history?latest=true&tz=UTC&" + range)).Should().Equal(late);
        // In Tashkent it falls on the 8th, so the 7th ends on the fix and the four failures before it hide.
        Ids(await DataAsync("/api/deployments/history?latest=true&tz=Asia/Tashkent&" + range)).Should().Equal(late, fixedDev);
        Ids(await DataAsync("/api/deployments/history?latest=true&notApplied=true&tz=Asia/Tashkent&" + range)).Should()
            .Equal(late, refused, fixedDev);

        Attention(await DataAsync("/api/deployments/history/activity?latest=true&tz=Asia/Tashkent&" + range)).Should()
            .Equal(("2026-10-07", 1, 0, 0, 0), ("2026-10-08", 0, 0, 1, 0));
        Attention(await DataAsync("/api/deployments/history/activity?latest=true&tz=UTC&" + range)).Should()
            .Equal(("2026-10-07", 0, 0, 1, 0));
        Attention(await DataAsync("/api/deployments/history/activity?notApplied=true&tz=Asia/Tashkent&" + range)).Should()
            .Equal(("2026-10-07", 1, 0, 4, 1), ("2026-10-08", 0, 0, 1, 0));

        // The measures follow the view: every attempt, the refusal included, or each day's latest alone; either way they
        // say how many a latest-of-day view hides.
        (int, int, int, int, int) Measures(JsonElement stats) => (stats.GetProperty("deploys").GetInt32(),
            stats.GetProperty("succeeded").GetInt32(), stats.GetProperty("act").GetInt32(),
            stats.GetProperty("notApplied").GetInt32(), stats.GetProperty("superseded").GetInt32());
        var every = (await DataAsync("/api/deployments/history/stats?notApplied=true&tz=Asia/Tashkent&" + range)).GetProperty("current");
        Measures(every).Should().Be((7, 1, 5, 1, 4));
        every.GetProperty("successRate").GetDouble().Should().BeApproximately(1d / 6, 1e-9);
        var latestOnly = (await DataAsync("/api/deployments/history/stats?latest=true&tz=Asia/Tashkent&" + range)).GetProperty("current");
        Measures(latestOnly).Should().Be((2, 1, 1, 1, 4));
        latestOnly.GetProperty("successRate").GetDouble().Should().Be(0.5);
        Measures((await DataAsync("/api/deployments/history/stats?latest=true&tz=UTC&" + range)).GetProperty("current"))
            .Should().Be((1, 0, 1, 1, 5));
    }

    [Fact]
    public async Task Latest_RefusesAnUnknownZone()
    {
        await SeedAsync();
        (await AdminClient.GetAsync("/api/deployments/history?latest=true&tz=Mars/Olympus")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("tz=Mars/Olympus")]
    [InlineData("from=2024-01-01T00:00:00Z&to=2026-10-01T00:00:00Z")]
    public async Task Activity_RefusesAnUnknownZoneOrARangeBeyondTwoYears(string query)
    {
        await SeedAsync();
        (await AdminClient.GetAsync("/api/deployments/history/activity?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stats_MeasureTheEntitiesBesideThePriorRangeAndIgnoreTheStatusFilter()
    {
        await SeedAsync();
        var stats = await DataAsync(
            "/api/deployments/history/stats?product=foreverpin&status=failed&from=2026-10-04T00:00:00Z&to=2026-10-08T00:00:00Z");
        var current = stats.GetProperty("current");
        (current.GetProperty("deploys").GetInt32(), current.GetProperty("succeeded").GetInt32(), current.GetProperty("failed").GetInt32(),
                current.GetProperty("refused").GetInt32(), current.GetProperty("pending").GetInt32())
            .Should().Be((2, 1, 0, 0, 1), "the refusal changed nothing, so it counts only when asked for");
        (current.GetProperty("notApplied").GetInt32(), current.GetProperty("look").GetInt32(), current.GetProperty("act").GetInt32())
            .Should().Be((1, 1, 0), "the hidden refusal is counted, so the console can say how many it hides");
        var counted = (await DataAsync(
            "/api/deployments/history/stats?product=foreverpin&notApplied=true&from=2026-10-04T00:00:00Z&to=2026-10-08T00:00:00Z")).GetProperty("current");
        (counted.GetProperty("deploys").GetInt32(), counted.GetProperty("refused").GetInt32(), counted.GetProperty("notApplied").GetInt32())
            .Should().Be((3, 1, 1));
        current.GetProperty("successRate").GetDouble().Should().Be(1);
        current.GetProperty("medianRolloutSeconds").GetDouble().Should().Be(150);

        // The four days before: #1, #2 and the failure #3, which #5 recovered from in the current range only.
        var previous = stats.GetProperty("previous");
        (previous.GetProperty("deploys").GetInt32(), previous.GetProperty("succeeded").GetInt32(), previous.GetProperty("failed").GetInt32())
            .Should().Be((3, 2, 1));
        previous.GetProperty("successRate").GetDouble().Should().BeApproximately(2d / 3, 1e-9);

        var whole = (await DataAsync(
            "/api/deployments/history/stats?product=foreverpin&from=2026-10-01T00:00:00Z&to=2026-10-08T00:00:00Z")).GetProperty("current");
        // fp-dev failed at 10-03 23:32:00 and recovered at 10-05 10:02:40.
        whole.GetProperty("medianRecoverySeconds").GetDouble().Should().Be(TimeSpan.Parse("1.10:30:40").TotalSeconds);

        var empty = await DataAsync("/api/deployments/history/stats?workspace=nobody");
        empty.GetProperty("current").GetProperty("deploys").GetInt32().Should().Be(0);
        empty.GetProperty("current").GetProperty("successRate").ValueKind.Should().Be(JsonValueKind.Null);
        empty.GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Running_ListsLiveRolloutsWithTheirStepAndFollowsTheEntities()
    {
        await SeedAsync();
        var running = await DataAsync("/api/deployments/running");
        running.EnumerateArray().Select(item => item.GetProperty("id").GetString()).Should().Equal(Running);
        var step = running[0].GetProperty("currentStep");
        (step.GetProperty("name").GetString(), step.GetProperty("index").GetInt32()).Should().Be(("Pull images", 2));
        (await DataAsync("/api/deployments/running?workspace=ocharo")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Promotions_OfferTheNewestDevReleaseProdDoesNotRun()
    {
        await SeedAsync();
        // A newer commit build on dev is never offered to prod.
        Fixture.Deployments.History.Add(Record("00000000-0000-4000-8000-000000000010", "foreverpin-dev", "2026-10-06T08:00:00+00:00",
            "succeeded", "sha-abc1234", "foreverpin-ci-9", WebB, kind: "candidate", published: "2026-10-06T07:00:00Z"));

        var promotions = await DataAsync("/api/deployments/promotions");
        promotions.GetArrayLength().Should().Be(1);
        var offer = promotions[0];
        (offer.GetProperty("product").GetString(), offer.GetProperty("release").GetString(), offer.GetProperty("bundleId").GetString(),
                offer.GetProperty("devTargetId").GetString(), offer.GetProperty("prodTargetId").GetString(),
                offer.GetProperty("prodRelease").GetString(), offer.GetProperty("workspace").GetString())
            .Should().Be(("foreverpin", "v1.1.0", "foreverpin-gh-2", "foreverpin-dev", "foreverpin-prod", "v1.0.0", "own-ventures"));
        (await DataAsync("/api/deployments/promotions?workspace=ocharo")).GetArrayLength().Should().Be(0);

        // Once prod runs it, nothing is left to promote.
        Fixture.Deployments.History.Add(Record("00000000-0000-4000-8000-000000000011", "foreverpin-prod", "2026-10-07T08:00:00+00:00",
            "succeeded", "v1.1.0", "foreverpin-gh-2", WebA, management: ManagementB, published: "2026-10-03T12:00:00Z"));
        (await DataAsync("/api/deployments/promotions")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Promotions_LeaveOutAProductWheelhouseDoesNotDeploy()
    {
        await SeedAsync();
        (await DataAsync("/api/deployments/promotions")).GetArrayLength().Should().Be(1);

        await ProductDeployModes.SetAsync(AdminClient, "foreverpin", "ci");
        (await DataAsync("/api/deployments/promotions")).GetArrayLength().Should().Be(0, "its own CI promotes it");
        await ProductDeployModes.SetAsync(AdminClient, "foreverpin", "none");
        (await DataAsync("/api/deployments/promotions")).GetArrayLength().Should().Be(0);
        await ProductDeployModes.SetAsync(AdminClient, "foreverpin", "wheelhouse");
        (await DataAsync("/api/deployments/promotions")).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Promotions_LeaveOutAReleaseAlreadyOnItsWayToProd()
    {
        await SeedAsync();
        Fixture.Deployments.History.Add(Record("00000000-0000-4000-8000-000000000012", "foreverpin-prod", "2026-10-07T08:00:00+00:00",
            "running", "v1.1.0", "foreverpin-gh-2", WebA, management: ManagementB));
        (await DataAsync("/api/deployments/promotions")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Issues_ListFailuresWithNoSuccessSinceAndStuckRollouts_ThoseNeedingAReconcileFirst()
    {
        await SeedIssuesAsync(
            // fp-dev: a success, then a rollout live for two hours — stuck.
            Record(Issue(1), "foreverpin-dev", Ago(TimeSpan.FromDays(3)), "succeeded", "v1.0.0", "foreverpin-gh-1", WebA),
            Record(Issue(2), "foreverpin-dev", Ago(TimeSpan.FromHours(2)), "running", "v1.1.0", "foreverpin-gh-2", WebB),
            // fp-prod: a rollback, then a refusal that changed nothing — the rollback still stands.
            Record(Issue(3), "foreverpin-prod", Ago(TimeSpan.FromDays(5)), "succeeded", "v1.0.0", "foreverpin-gh-1", WebA),
            Record(Issue(4), "foreverpin-prod", Ago(TimeSpan.FromDays(1)), "rolled_back", "v1.1.0", "foreverpin-gh-2", WebB),
            Record(Issue(5), "foreverpin-prod", Ago(TimeSpan.FromHours(1)), "rejected", "v1.1.0", "foreverpin-gh-2", WebB),
            // The shop: a failure that had replaced containers, so the runner refuses the next rollout until a reconcile.
            new
            {
                id = Issue(6), targetId = "ocharo-shop-dev", bundleId = "ocharo-shop-gh-3", release = "v0.3.0", actor = "key:release-bot",
                status = "failed", submittedAt = Ago(TimeSpan.FromHours(6)), startedAt = Ago(TimeSpan.FromHours(6) - TimeSpan.FromSeconds(5)),
                completedAt = Ago(TimeSpan.FromHours(5)), mutationStarted = true, reason = "web did not become healthy",
            },
            // A removed target's failure: nothing can be redeployed there.
            new { id = Issue(7), targetId = "gone-dev", bundleId = "gone-gh-1", release = "v0.1.0", status = "failed", submittedAt = Ago(TimeSpan.FromHours(3)) });

        var issues = await DataAsync("/api/deployments/issues");
        Issues(issues).Should().Equal(("stuck", true, Issue(2)), ("failed", true, Issue(6)), ("rolled_back", false, Issue(4)));
        issues[0].GetProperty("at").GetDateTimeOffset().Should().Be(Fixture.Host.Clock.GetUtcNow() - TimeSpan.FromHours(2) + TimeSpan.FromSeconds(10));
        issues[1].GetProperty("at").GetDateTimeOffset().Should().Be(Fixture.Host.Clock.GetUtcNow() - TimeSpan.FromHours(5));
        var failed = issues[1].GetProperty("deployment");
        (failed.GetProperty("product").GetString(), failed.GetProperty("environment").GetString(), failed.GetProperty("release").GetString(),
                failed.GetProperty("reason").GetString(), failed.GetProperty("actor").GetString())
            .Should().Be(("ocharo-shop", "dev", "v0.3.0", "web did not become healthy", "key:release-bot"));
        Issues(await DataAsync("/api/deployments/issues?workspace=ocharo")).Should().Equal(("failed", true, Issue(6)));
    }

    [Fact]
    public async Task Issues_ClearOnALaterSuccessAndLeaveARolloutOnTimeAlone()
    {
        await SeedIssuesAsync(
            Record(Issue(1), "foreverpin-dev", Ago(TimeSpan.FromDays(1)), "failed", "v1.1.0", "foreverpin-gh-2", WebB),
            Record(Issue(2), "foreverpin-dev", Ago(TimeSpan.FromMinutes(5)), "running", "v1.1.1", "foreverpin-gh-3", WebB),
            Record(Issue(3), "foreverpin-prod", Ago(TimeSpan.FromDays(2)), "rolled_back", "v1.1.0", "foreverpin-gh-2", WebB),
            Record(Issue(4), "foreverpin-prod", Ago(TimeSpan.FromDays(1)), "succeeded", "v1.0.1", "foreverpin-gh-5", WebA),
            // Reconciled after it stopped mid-way, with nothing deployed since: still an issue, no longer locked.
            new
            {
                id = Issue(5), targetId = "ocharo-shop-dev", bundleId = "ocharo-shop-gh-3", release = "v0.3.0", actor = "max",
                status = "interrupted", submittedAt = Ago(TimeSpan.FromHours(4)), completedAt = Ago(TimeSpan.FromHours(3)),
                mutationStarted = true,
            });

        Issues(await DataAsync("/api/deployments/issues")).Should().Equal(("interrupted", false, Issue(5)));
    }

    /// <summary>Seeds the inventory, then replaces the eight dated records with ones relative to the host's fake clock.</summary>
    private async Task SeedIssuesAsync(params object[] records)
    {
        await SeedAsync();
        Fixture.Deployments.History.Clear();
        Fixture.Deployments.History.AddRange(records);
    }

    /// <summary>An instant this long before the host's fake clock, as the runner writes it.</summary>
    private string Ago(TimeSpan span) =>
        (Fixture.Host.Clock.GetUtcNow() - span).ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static string Issue(int number) => $"00000000-0000-4000-8000-0000000001{number:D2}";

    [Fact]
    public async Task Detail_ShowsServicesFromAndToStepsSitesAndTheRollBack()
    {
        await SeedAsync();
        Fixture.Deployments.Observations[SecondDev] = new
        {
            id = SecondDev, status = "succeeded",
            steps = new object[]
            {
                new { name = "Check target", status = "succeeded", startedAt = "2026-10-05T10:00:00+00:00", completedAt = "2026-10-05T10:00:01+00:00" },
                new { name = "Probe sites", status = "warning", detail = "0 of 1 site answered" },
                new { status = "succeeded" },
            },
            sites = new object[]
            {
                new { name = "app", service = "web", exposure = "public", url = "https://dev.foreverpin.example", probe = new { ok = false, detail = "timed out" } },
                new { name = "evil", service = "web", exposure = "public", url = "javascript:alert(1)" },
            },
            warnings = new[] { "Site app did not answer through the ingress: timed out" },
        };

        var detail = await DataAsync("/api/deployments/" + SecondDev + "/detail");
        detail.GetProperty("deployment").GetProperty("release").GetString().Should().Be("v1.1.0");
        detail.GetProperty("observed").GetBoolean().Should().BeTrue();
        detail.GetProperty("services").EnumerateArray()
            .Select(item => (item.GetProperty("service").GetString(), item.GetProperty("from").GetString(),
                item.GetProperty("to").GetString(), item.GetProperty("changed").GetBoolean()))
            .Should().Equal(("management", "1.0.0", "1.1.0", true), ("web", "cccccccccccc", "cccccccccccc", false));
        detail.GetProperty("previous").GetProperty("id").GetString().Should().Be(FirstDev);
        detail.GetProperty("rollback").GetProperty("bundleId").GetString().Should().Be("foreverpin-gh-1");
        detail.GetProperty("steps").GetArrayLength().Should().Be(2);
        detail.GetProperty("steps")[1].GetProperty("detail").GetString().Should().Be("0 of 1 site answered");
        detail.GetProperty("sites").GetArrayLength().Should().Be(1);
        detail.GetProperty("sites")[0].GetProperty("probe").GetProperty("ok").GetBoolean().Should().BeFalse();
        Fixture.Deployments.LastRead.Should().Be(("observation", SecondDev));
    }

    [Fact]
    public async Task Detail_OffersNoRollBackOnceALaterReleaseRunsAndAnswersWithoutTheTargetsReport()
    {
        await SeedAsync();
        // #1 is no longer what fp-dev runs, and no observation is held for it.
        var detail = await DataAsync("/api/deployments/" + FirstDev + "/detail");
        detail.GetProperty("rollback").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("observed").GetBoolean().Should().BeFalse();
        detail.GetProperty("steps").GetArrayLength().Should().Be(0);
        detail.GetProperty("services").EnumerateArray().Select(item => item.GetProperty("from").ValueKind)
            .Should().AllSatisfy(kind => kind.Should().Be(JsonValueKind.Null));
    }

    [Fact]
    public async Task Detail_IsNotFoundForAnUnknownDeployment()
    {
        await SeedAsync();
        (await AdminClient.GetAsync("/api/deployments/00000000-0000-4000-8000-0000000000ff/detail")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reads_CarryTheRunnersFailure()
    {
        await SeedAsync();
        Fixture.Deployments.ReadFailure = AppErrorFactory.ExternalUnavailable("Deployment runner is unavailable.");
        var response = await AdminClient.GetAsync("/api/deployments/history");
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Deployment runner is unavailable.");
    }

    [Fact]
    public async Task History_SkipsAMalformedRecord()
    {
        await SeedAsync();
        Fixture.Deployments.History.Add(new { id = "broken", status = "succeeded", submittedAt = "not a time" });
        Fixture.Deployments.History.Add(new { targetId = "foreverpin-dev" });
        Ids(await DataAsync("/api/deployments/history?notApplied=true")).Should().HaveCount(8);
    }

    /// <summary>Adds the Ocharo workspace with its shop on dev, and writes the eight records.</summary>
    private async Task SeedAsync()
    {
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var workspace = await services.GetRequiredService<IWorkspacesRepository>().CreateAsync(new WorkspaceEntity
            {
                Id = Guid.NewGuid(), Slug = "ocharo", Name = "Ocharo", Order = 1,
            });
            var shop = await services.GetRequiredService<IProductsRepository>().CreateAsync(new ProductEntity
            {
                Id = Guid.NewGuid(), Slug = "ocharo-shop", Name = "Ocharo shop", Description = "Shawls.",
                Repository = "ocharo-hq/ocharo-shop", DefaultBranch = "main", WorkspaceId = workspace.Id,
            });
            var server = (await services.GetRequiredService<IServersRepository>().GetAllAsync()).First(row => row.Slug == "pilot-host");
            await services.GetRequiredService<ITargetsRepository>().CreateAsync(new TargetEntity
            {
                Id = Guid.NewGuid(), Slug = "ocharo-shop-dev", ProductId = shop.Id, ServerId = server.Id,
                Environment = DeploymentEnvironment.Dev, Network = "platform", Root = "/srv/wheelhouse",
            });
        }

        var history = Fixture.Deployments.History;
        history.Add(Record(FirstDev, "foreverpin-dev", "2026-10-01T10:00:00+00:00", "succeeded", "v1.0.0", "foreverpin-gh-1",
            WebA, published: "2026-09-30T12:00:00Z", managementVersion: "1.0.0"));
        history.Add(Record(FirstProd, "foreverpin-prod", "2026-10-02T10:00:00+00:00", "succeeded", "v1.0.0", "foreverpin-gh-1",
            WebA, published: "2026-09-30T12:00:00Z", managementVersion: "1.0.0"));
        history.Add(Record(FailedDev, "foreverpin-dev", "2026-10-03T23:30:00+00:00", "failed", "v1.1.0", "foreverpin-gh-2",
            WebA, management: ManagementB, published: "2026-10-03T12:00:00Z", managementVersion: "1.1.0",
            completed: "2026-10-03T23:32:00+00:00", mutation: true));
        history.Add(new
        {
            id = RolledBack, targetId = "ocharo-shop-dev", bundleId = "ocharo-shop-gh-3", release = "v0.3.0", actor = "max",
            status = "rolled_back", submittedAt = "2026-10-04T08:00:00+00:00", startedAt = "2026-10-04T08:00:10+00:00",
            completedAt = "2026-10-04T08:03:00+00:00", product = "ocharo-shop", kind = "release", rollbackCompatible = true,
            reason = "Smoke check failed",
            services = new Dictionary<string, object> { ["api"] = new { image = "ghcr.io/o/shop/api@sha256:" + new string('e', 64), version = "0.3.0" } },
        });
        history.Add(Record(SecondDev, "foreverpin-dev", "2026-10-05T10:00:00+00:00", "succeeded", "v1.1.0", "foreverpin-gh-2",
            WebA, management: ManagementB, published: "2026-10-03T12:00:00Z", managementVersion: "1.1.0",
            warnings: ["Site app did not answer through the ingress: timed out"]));
        history.Add(Record(RefusedProd, "foreverpin-prod", "2026-10-05T12:00:00+00:00", "rejected", "v1.1.0", "foreverpin-gh-2",
            WebA, management: ManagementB, published: "2026-10-03T12:00:00Z", managementVersion: "1.1.0"));
        history.Add(new
        {
            id = Running, targetId = "foreverpin-dev", bundleId = "foreverpin-gh-3", release = "v1.2.0", actor = "max", status = "running",
            submittedAt = "2026-10-06T10:00:00+00:00", startedAt = "2026-10-06T10:00:05+00:00", product = "foreverpin",
            kind = "release", rollbackCompatible = true,
            services = Services(ManagementB, WebB, "1.1.0"),
            currentStep = new { name = "Pull images", status = "running", startedAt = "2026-10-06T10:00:06+00:00", index = 2 },
        });
        history.Add(new
        {
            id = Removed, targetId = "gone-dev", environment = "dev", bundleId = "gone-gh-1", release = "v0.1.0", actor = "max", status = "succeeded",
            submittedAt = "2026-09-01T10:00:00+00:00", product = (string?)null, services = (object?)null,
        });
    }

    /// <summary>A ForeverPin record; started 10 seconds and finished 150 seconds after its submission unless told otherwise.</summary>
    private static object Record(string id, string target, string submitted, string status, string release, string bundle, string web,
        string? management = null, string kind = "release", string? published = null, string? managementVersion = null,
        string? completed = null, string[]? warnings = null, bool? mutation = null)
    {
        var at = DateTimeOffset.Parse(submitted, System.Globalization.CultureInfo.InvariantCulture);
        var finished = status is "succeeded" or "failed" or "rolled_back"
            ? completed ?? at.AddSeconds(160).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            : null;
        return new
        {
            id, targetId = target, bundleId = bundle, release, actor = "max", status, submittedAt = submitted,
            startedAt = status == "rejected" ? null : at.AddSeconds(10).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            completedAt = finished, product = "foreverpin", kind, rollbackCompatible = true, publishedAt = published,
            warnings = warnings ?? [], services = Services(management ?? ManagementA, web, managementVersion),
            mutationStarted = mutation,
        };
    }

    private static Dictionary<string, object> Services(string management, string web, string? managementVersion) => new()
    {
        ["management"] = new { image = management, version = managementVersion },
        ["web"] = new { image = web, version = (string?)null },
    };

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static IEnumerable<string?> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();

    private static JsonElement Item(JsonElement page, string id) =>
        page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);

    private static IEnumerable<(string?, bool, string?)> Issues(JsonElement issues) =>
        issues.EnumerateArray()
            .Select(issue => (issue.GetProperty("kind").GetString(), issue.GetProperty("needsReconcile").GetBoolean(),
                issue.GetProperty("deployment").GetProperty("id").GetString()))
            .ToArray();

    private static IEnumerable<(string?, int, int)> Products(JsonElement activity) =>
        activity.GetProperty("products").EnumerateArray()
            .Select(item => (item.GetProperty("product").GetString(), item.GetProperty("succeeded").GetInt32(), item.GetProperty("failed").GetInt32()))
            .ToArray();

    private static IEnumerable<(string?, string?, string?)> States(JsonElement page) =>
        page.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("state").GetString(),
                item.GetProperty("substate").ValueKind == JsonValueKind.Null ? null : item.GetProperty("substate").GetString(),
                item.GetProperty("attention").GetString()))
            .ToArray();

    private static IEnumerable<(string?, int, int, int, int)> Attention(JsonElement activity) =>
        activity.GetProperty("days").EnumerateArray()
            .Select(day => (day.GetProperty("date").GetString(), day.GetProperty("calm").GetInt32(), day.GetProperty("look").GetInt32(),
                day.GetProperty("act").GetInt32(), day.GetProperty("notApplied").GetInt32()))
            .ToArray();

    private static IEnumerable<(string?, int, int)> Days(JsonElement activity) =>
        activity.GetProperty("days").EnumerateArray()
            .Select(day => (day.GetProperty("date").GetString(), day.GetProperty("succeeded").GetInt32(), day.GetProperty("failed").GetInt32()))
            .ToArray();
}
