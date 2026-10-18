using System.Globalization;
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
/// E2E for the Products page's portfolio summary: per product and environment the latest rollout (refused ones skipped)
/// and the last success, finished rollouts per Monday-first week in the owner's time zone, and each workspace's
/// succeeded and failed rollouts per week.
/// </summary>
/// <remarks>
/// Read as of Wednesday 2026-10-07 12:00 UTC over three weeks (Mondays 09-21, 09-28, 10-05). ForeverPin (own-ventures)
/// has dev and prod, Ocharo's shop (ocharo) dev only.
/// <list type="number">
/// <item>fp-dev v0.9.0 succeeded 09-01 — before the weeks, so latest nowhere and counted nowhere.</item>
/// <item>fp-dev v1.0.0 succeeded 09-22; fp-prod v1.0.0 succeeded 09-23.</item>
/// <item>fp-dev v1.1.0 failed Sunday 10-04 23:30 UTC — Monday 10-05 in Tashkent, so the next week there.</item>
/// <item>shop-dev v0.3.0 rolled back 10-05.</item>
/// <item>fp-dev v1.1.0 succeeded 10-06; fp-prod v1.1.0 refused 10-06.</item>
/// <item>fp-dev v1.2.0 running 10-07 10:00; fp-dev v1.3.0 succeeded 10-08 — after the summary's instant.</item>
/// <item>gone-dev, a removed target whose product the inventory does not hold, 10-06.</item>
/// </list>
/// </remarks>
[Collection(WheelhouseCollection.Name)]
public sealed class PortfolioSummaryE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Path = "/api/portfolio/summary?weeks=3&to=2026-10-07T12:00:00Z";
    private const string Early = "00000000-0000-4000-8000-000000000301";
    private const string FirstDev = "00000000-0000-4000-8000-000000000302";
    private const string FirstProd = "00000000-0000-4000-8000-000000000303";
    private const string FailedDev = "00000000-0000-4000-8000-000000000304";
    private const string RolledBack = "00000000-0000-4000-8000-000000000305";
    private const string SecondDev = "00000000-0000-4000-8000-000000000306";
    private const string RefusedProd = "00000000-0000-4000-8000-000000000307";
    private const string Running = "00000000-0000-4000-8000-000000000308";
    private const string Later = "00000000-0000-4000-8000-000000000309";
    private const string Removed = "00000000-0000-4000-8000-000000000310";

    [Fact]
    public async Task Summary_RequiresTheOwner()
    {
        await SeedAsync();
        (await AnonymousClient.GetAsync(Path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient.GetAsync(Path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Summary_GivesEachEnvironmentItsLatestAndServingRolloutAndWeeklyCounts()
    {
        await SeedAsync();
        var summary = await DataAsync(Path);

        summary.GetProperty("timeZone").GetString().Should().Be("UTC");
        Strings(summary.GetProperty("weeks")).Should().Equal("2026-09-21", "2026-09-28", "2026-10-05");
        summary.GetProperty("products").EnumerateArray().Select(item => item.GetProperty("product").GetString())
            .Should().Equal("foreverpin", "ocharo-shop");

        var foreverPin = Product(summary, "foreverpin");
        (foreverPin.GetProperty("workspace").GetString(), foreverPin.GetProperty("succeeded").GetInt32(),
                foreverPin.GetProperty("failed").GetInt32())
            .Should().Be(("own-ventures", 3, 1));
        foreverPin.GetProperty("environments").EnumerateArray().Select(item => item.GetProperty("environment").GetString())
            .Should().Equal("dev", "prod");

        // Dev: the live rollout is the latest, the last success serves; the 10-08 success is after the instant.
        var dev = Environment(foreverPin, "dev");
        Weekly(dev).Should().Equal(1, 1, 1);
        var latest = dev.GetProperty("lastDeploy");
        (latest.GetProperty("id").GetString(), latest.GetProperty("release").GetString(), latest.GetProperty("status").GetString(),
                latest.GetProperty("outcome").GetString(), latest.GetProperty("actor").GetString())
            .Should().Be((Running, "v1.2.0", "running", "pending", "max"));
        latest.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);
        dev.GetProperty("running").GetProperty("id").GetString().Should().Be(SecondDev);

        // Prod: the refused submission changed nothing, so the first release is both latest and serving.
        var prod = Environment(foreverPin, "prod");
        Weekly(prod).Should().Equal(1, 0, 0);
        prod.GetProperty("lastDeploy").GetProperty("id").GetString().Should().Be(FirstProd);
        prod.GetProperty("running").GetProperty("release").GetString().Should().Be("v1.0.0");

        // The shop rolled back with no success before: latest failed, nothing serving.
        var shop = Environment(Product(summary, "ocharo-shop"), "dev");
        Weekly(shop).Should().Equal(0, 0, 1);
        (shop.GetProperty("lastDeploy").GetProperty("status").GetString(), shop.GetProperty("lastDeploy").GetProperty("outcome").GetString(),
                shop.GetProperty("lastDeploy").GetProperty("reason").GetString())
            .Should().Be(("rolled_back", "failed", "Smoke check failed"));
        shop.GetProperty("running").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Summary_CountsEachWorkspacesOutcomesPerWeek()
    {
        await SeedAsync();
        var summary = await DataAsync(Path);

        Workspaces(summary).Should().Equal(
            ("ocharo", 0, 1, "0/0 0/0 0/1"),
            ("own-ventures", 3, 1, "2/0 0/1 1/0"));
    }

    [Fact]
    public async Task Summary_StartsWeeksOnMondayInTheOwnersTimeZone()
    {
        await SeedAsync();
        var summary = await DataAsync(Path + "&tz=Asia/Tashkent");

        summary.GetProperty("timeZone").GetString().Should().Be("Asia/Tashkent");
        // The Sunday-night failure is Monday morning in Tashkent, so it joins the current week.
        Weekly(Environment(Product(summary, "foreverpin"), "dev")).Should().Equal(1, 0, 2);
        Workspaces(summary).Last().Should().Be(("own-ventures", 3, 1, "2/0 0/0 1/1"));
    }

    [Fact]
    public async Task Summary_EndsWithTheCurrentWeekOnTheHostsClock_WhenNoInstantIsGiven()
    {
        await SeedAsync();
        var summary = await DataAsync("/api/portfolio/summary");

        var today = DateOnly.FromDateTime(Fixture.Host.Clock.GetUtcNow().UtcDateTime);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weeks = Strings(summary.GetProperty("weeks")).ToList();
        weeks.Should().HaveCount(12);
        weeks[^1].Should().Be(monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        weeks[0].Should().Be(monday.AddDays(-77).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("weeks=0")]
    [InlineData("weeks=53")]
    [InlineData("tz=Mars/Base")]
    public async Task Summary_Returns400_ForWeeksOrATimeZoneItCannotCount(string query)
    {
        (await AdminClient.GetAsync("/api/portfolio/summary?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Summary_CarriesTheRunnersFailure()
    {
        await SeedAsync();
        Fixture.Deployments.ReadFailure = AppErrorFactory.ExternalUnavailable("Deployment runner is unavailable.");

        var response = await AdminClient.GetAsync(Path);

        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Deployment runner is unavailable.");
    }

    /// <summary>Adds the Ocharo workspace with its shop on dev, and writes the records.</summary>
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

        Fixture.Deployments.History.AddRange(
        [
            Record(Early, "foreverpin-dev", "2026-09-01T10:00:00+00:00", "succeeded", "v0.9.0"),
            Record(FirstDev, "foreverpin-dev", "2026-09-22T10:00:00+00:00", "succeeded", "v1.0.0"),
            Record(FirstProd, "foreverpin-prod", "2026-09-23T10:00:00+00:00", "succeeded", "v1.0.0"),
            Record(FailedDev, "foreverpin-dev", "2026-10-04T23:30:00+00:00", "failed", "v1.1.0"),
            new
            {
                id = RolledBack, targetId = "ocharo-shop-dev", bundleId = "ocharo-shop-gh-3", release = "v0.3.0", actor = "max",
                status = "rolled_back", submittedAt = "2026-10-05T08:00:00+00:00", completedAt = "2026-10-05T08:03:00+00:00",
                product = "ocharo-shop", kind = "release", reason = "Smoke check failed",
            },
            Record(SecondDev, "foreverpin-dev", "2026-10-06T10:00:00+00:00", "succeeded", "v1.1.0"),
            Record(RefusedProd, "foreverpin-prod", "2026-10-06T12:00:00+00:00", "rejected", "v1.1.0"),
            Record(Running, "foreverpin-dev", "2026-10-07T10:00:00+00:00", "running", "v1.2.0"),
            Record(Later, "foreverpin-dev", "2026-10-08T10:00:00+00:00", "succeeded", "v1.3.0"),
            new
            {
                id = Removed, targetId = "gone-dev", environment = "dev", bundleId = "gone-gh-1", release = "v0.1.0", actor = "max",
                status = "succeeded", submittedAt = "2026-10-06T10:00:00+00:00", product = (string?)null,
            },
        ]);
    }

    /// <summary>A ForeverPin record; a finished one completes three minutes after its submission.</summary>
    private static object Record(string id, string target, string submitted, string status, string release)
    {
        var at = DateTimeOffset.Parse(submitted, CultureInfo.InvariantCulture);
        return new
        {
            id, targetId = target, bundleId = "foreverpin-" + release, release, actor = "max", status, submittedAt = submitted,
            completedAt = status is "succeeded" or "failed" ? at.AddMinutes(3).ToString("O", CultureInfo.InvariantCulture) : null,
            product = "foreverpin", kind = "release",
        };
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static IEnumerable<string?> Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()).ToArray();

    private static JsonElement Product(JsonElement summary, string slug) =>
        summary.GetProperty("products").EnumerateArray().Single(item => item.GetProperty("product").GetString() == slug);

    private static JsonElement Environment(JsonElement product, string environment) =>
        product.GetProperty("environments").EnumerateArray().Single(item => item.GetProperty("environment").GetString() == environment);

    private static IEnumerable<int> Weekly(JsonElement environment) =>
        environment.GetProperty("weekly").EnumerateArray().Select(item => item.GetInt32()).ToArray();

    /// <summary>Each workspace as (slug, succeeded, failed, "succeeded/failed" per week).</summary>
    private static IEnumerable<(string?, int, int, string)> Workspaces(JsonElement summary) =>
        summary.GetProperty("workspaces").EnumerateArray()
            .Select(item => (item.GetProperty("workspace").GetString(), item.GetProperty("succeeded").GetInt32(),
                item.GetProperty("failed").GetInt32(),
                string.Join(' ', item.GetProperty("weekly").EnumerateArray()
                    .Select(week => $"{week.GetProperty("succeeded").GetInt32()}/{week.GetProperty("failed").GetInt32()}"))))
            .ToArray();
}
