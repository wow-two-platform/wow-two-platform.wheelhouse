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
