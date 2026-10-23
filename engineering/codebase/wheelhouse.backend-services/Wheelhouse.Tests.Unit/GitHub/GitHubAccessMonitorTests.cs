using AwesomeAssertions;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.GitHub;

/// <summary>Tests for <see cref="GitHubAccessMonitor"/>: the console's and the runner's notes merged per owner, and how
/// long a note stays.</summary>
public sealed class GitHubAccessMonitorTests : IDisposable
{
    private readonly GitHubAppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void Current_ShouldListTheRunnersRefusedCallsPerOwner_SkippingWhatItCannotTrust()
    {
        var at = _rig.Clock.GetUtcNow().AddMinutes(-5).ToString("O");
        WriteRunnerNotes($$$"""
            {"owners": {
              "ocharo-hq": {"status": 429, "authenticated": true, "rateLimited": true, "at": "{{{at}}}"},
              "bad owner": {"status": 403, "authenticated": false, "rateLimited": false, "at": "{{{at}}}"},
              "sulton-max": {"status": 500, "authenticated": true, "rateLimited": false, "at": "{{{at}}}"}
            }}
            """);

        var issue = _rig.Monitor.Current().Should().ContainSingle().Subject;

        (issue.Owner, issue.Status, issue.Cause).Should().Be(
            ("ocharo-hq", (int?)429, GitHubAccessCauses.Refused(429, authenticated: true, rateLimited: true)));
    }

    [Fact]
    public void Current_ShouldKeepTheNewerOfTheConsolesAndTheRunnersNoteForAnOwner()
    {
        var at = _rig.Clock.GetUtcNow().AddMinutes(-10).ToString("O");
        WriteRunnerNotes($$$"""
            {"owners": {"wow-two": {"status": 403, "authenticated": false, "rateLimited": true, "at": "{{{at}}}"}
            }}
            """);
        _rig.Monitor.Refused("wow-two", 401, authenticated: true, rateLimited: false);

        _rig.Monitor.Current().Should().ContainSingle().Which.Status.Should().Be(401);
    }

    [Fact]
    public void Current_ShouldDropARefusedCallNobodyRepeatedWithinAnHour_ButKeepTheAppsOwnProblemLonger()
    {
        _rig.Monitor.Refused("wow-two", 403, authenticated: false, rateLimited: true);
        _rig.Monitor.InstallationRefused("sulton-max", 403);

        _rig.Clock.Advance(GitHubAccessMonitor.CallKeptFor + TimeSpan.FromSeconds(1));

        _rig.Monitor.Current().Should().ContainSingle().Which.Owner.Should().Be("sulton-max");
    }

    [Fact]
    public void Current_ShouldNameARepositoryTheRunnerCouldNotSeeWithoutItsOwnersApp()
    {
        var at = _rig.Clock.GetUtcNow().AddMinutes(-1).ToString("O");
        WriteRunnerNotes($$$"""
            {"owners": {
              "sulton-max": {"status": 404, "authenticated": true, "installed": false, "rateLimited": false,
                             "repository": "sulton-max/private-app", "at": "{{{at}}}"},
              "wow-two": {"status": 404, "repository": "ocharo-hq/elsewhere", "at": "{{{at}}}"},
              "ocharo-hq": {"status": 403, "authenticated": true, "installed": false, "rateLimited": false, "at": "{{{at}}}"}
            }}
            """);

        var issues = _rig.Monitor.Current();

        issues.Select(issue => (issue.Owner, issue.Cause)).Should().BeEquivalentTo(
        [
            ("sulton-max", GitHubAccessCauses.Unseen("sulton-max/private-app")),
            ("ocharo-hq", GitHubAccessCauses.Refused(403, authenticated: true, rateLimited: false, installed: false)),
        ]);
    }

    [Fact]
    public void Current_ShouldNameEachPackageNoCredentialCouldRead_ForADayAndSkipWhatItCannotTrust()
    {
        var at = _rig.Clock.GetUtcNow().AddMinutes(-3).ToString("O");
        WritePackageNotes($$$"""
            {"packages": {
              "ocharo-hq/ocharo-assets/motion": {"status": 404, "product": "ocharo", "at": "{{{at}}}"},
              "bad owner/x": {"status": 404, "product": "ocharo", "at": "{{{at}}}"},
              "ocharo-hq/no-time": {"status": 404}
            }}
            """);

        var issue = _rig.Monitor.Current().Should().ContainSingle().Subject;

        (issue.Owner, issue.Status, issue.Cause).Should().Be(
            ("ocharo-hq", (int?)404, GitHubAccessCauses.PackageUnreadable("ocharo-hq/ocharo-assets/motion", "ocharo")));
        issue.Cause.Should().Contain("ghcr.io/ocharo-hq/ocharo-assets/motion").And.Contain("read:packages")
            .And.Contain("Deployment:GitHubTokenFile");

        _rig.Clock.Advance(GitHubAccessMonitor.SetupKeptFor);
        _rig.Monitor.Current().Should().BeEmpty();
    }

    [Fact]
    public void Current_ShouldIgnoreAMalformedRunnerFile()
    {
        WriteRunnerNotes("{not json");

        _rig.Monitor.Current().Should().BeEmpty();
    }

    private void WritePackageNotes(string json)
    {
        var path = GitHubRunnerFiles.PackagesPath(_rig.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private void WriteRunnerNotes(string json)
    {
        var path = GitHubRunnerFiles.AccessPath(_rig.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
