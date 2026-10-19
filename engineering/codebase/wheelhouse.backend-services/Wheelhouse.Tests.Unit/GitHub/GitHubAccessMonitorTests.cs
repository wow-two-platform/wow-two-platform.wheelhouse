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
    public void Current_ShouldDropARefusedCallNobodyRepeatedWithinAnHour()
    {
        _rig.Monitor.Refused("wow-two", 403, authenticated: false, rateLimited: true);
        _rig.Monitor.NotInstalled("wow-two");

        _rig.Clock.Advance(GitHubAccessMonitor.CallKeptFor + TimeSpan.FromSeconds(1));

        _rig.Monitor.Current().Should().ContainSingle().Which.Cause.Should().Be(GitHubAccessCauses.NotInstalled);
    }

    [Fact]
    public void Current_ShouldIgnoreAMalformedRunnerFile()
    {
        WriteRunnerNotes("{not json");

        _rig.Monitor.Current().Should().BeEmpty();
    }

    private void WriteRunnerNotes(string json)
    {
        var path = GitHubRunnerFiles.AccessPath(_rig.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
