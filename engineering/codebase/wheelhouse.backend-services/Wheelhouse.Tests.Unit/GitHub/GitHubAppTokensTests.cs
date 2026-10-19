using System.Net;
using AwesomeAssertions;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.GitHub;

/// <summary>Tests for <see cref="GitHubAppTokens"/>: the installation list, the token exchange, the per-owner cache and
/// its refresh, the token lent for public repositories, and the problems each refusal notes for the Issues panel.</summary>
public sealed class GitHubAppTokensTests : IDisposable
{
    private readonly GitHubAppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task GetAsync_ShouldFindTheOwnerInTheAppsInstallationsAndExchangeItWithTheAppToken()
    {
        _rig.Install("sulton-max", 11);

        var token = await _rig.Tokens.GetAsync("Sulton-Max", CancellationToken.None);

        token!.Token.Should().Be("ghs_sulton-max_1");
        _rig.GitHub.Calls.Select(call => call.Key).Should().Equal(
            "GET app/installations", "POST app/installations/11/access_tokens");
        _rig.GitHub.Calls.Should().AllSatisfy(call =>
        {
            call.Host.Should().Be("api.github.com");
            call.Authorization.Should().StartWith("Bearer ").And.Match("Bearer *.*.*", "each App call carries the signed App token");
        });
    }

    [Fact]
    public async Task GetAsync_ShouldListTheInstallationsOnceForEveryOwner()
    {
        _rig.Install("sulton-max", 11);
        _rig.Install("ocharo-hq", 22);

        var user = await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None);
        var organization = await _rig.Tokens.GetAsync("ocharo-hq", CancellationToken.None);

        (user!.Token, organization!.Token).Should().Be(("ghs_sulton-max_1", "ghs_ocharo-hq_1"));
        _rig.GitHub.CountOf("GET app/installations").Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_ShouldKeepATokenUntilFiveMinutesBeforeItsExpiry_ThenIssueANewOne()
    {
        _rig.Install("sulton-max", 11);

        var first = await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None);
        _rig.Clock.Advance(TimeSpan.FromMinutes(54));
        var kept = await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None);
        _rig.Clock.Advance(TimeSpan.FromMinutes(2));
        var renewed = await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None);

        kept.Should().BeSameAs(first);
        renewed!.Token.Should().Be("ghs_sulton-max_2");
        _rig.GitHub.CountOf("POST app/installations/11/access_tokens").Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldIssueOneTokenForConcurrentCallers()
    {
        _rig.Install("sulton-max", 11);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => _rig.Tokens.GetAsync("sulton-max", CancellationToken.None))));

        tokens.Select(token => token!.Token).Distinct().Should().Equal("ghs_sulton-max_1");
        _rig.GitHub.CountOf("GET app/installations").Should().Be(1);
        _rig.GitHub.CountOf("POST app/installations/11/access_tokens").Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_ShouldGiveNoTokenForAnOwnerWithoutTheApp_WithoutNamingIt()
    {
        _rig.Install("sulton-max", 11);

        (await _rig.Tokens.GetAsync("wow-two", CancellationToken.None)).Should().BeNull();
        (await _rig.Tokens.GetAsync("wow-two", CancellationToken.None)).Should().BeNull();

        _rig.GitHub.CountOf("GET app/installations").Should().Be(1, "the list is kept ten minutes");
        _rig.Tokens.IsInstalled("wow-two").Should().BeFalse();
        _rig.Monitor.Current().Should().BeEmpty("an owner without the App is named only when a repository needs it");

        _rig.Install("wow-two", 33);
        _rig.Clock.Advance(GitHubAppTokens.InstallationsFor);
        (await _rig.Tokens.GetAsync("wow-two", CancellationToken.None))!.Token.Should().Be("ghs_wow-two_1");
    }

    [Fact]
    public async Task BorrowAsync_ShouldLendAKeptToken_OrIssueOneFromAnyInstallation()
    {
        _rig.Install("sulton-max", 11);

        var lent = await _rig.Tokens.BorrowAsync(CancellationToken.None);
        var again = await _rig.Tokens.BorrowAsync(CancellationToken.None);

        (lent!.Token, again).Should().Be(("ghs_sulton-max_1", lent));
        _rig.GitHub.CountOf("POST app/installations/11/access_tokens").Should().Be(1);
    }

    [Fact]
    public async Task BorrowAsync_ShouldLendNothing_WhenTheAppIsInstalledNowhere()
    {
        (await _rig.Tokens.BorrowAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldNoteRefusedAppCredentials()
    {
        _rig.GitHub.Answers["GET app/installations"] = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        (await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None)).Should().BeNull();

        var issue = _rig.Monitor.Current().Should().ContainSingle().Subject;
        (issue.Owner, issue.Cause, issue.Status).Should().Be(((string?)null, GitHubAccessCauses.AppRefused, (int?)401));
    }

    [Fact]
    public async Task GetAsync_ShouldNoteASuspendedInstallation()
    {
        _rig.Install("sulton-max", 11);
        _rig.GitHub.Answers["POST app/installations/11/access_tokens"] = () => new HttpResponseMessage(HttpStatusCode.Forbidden);

        (await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None)).Should().BeNull();

        var issue = _rig.Monitor.Current().Should().ContainSingle().Subject;
        (issue.Owner, issue.Status).Should().Be(("sulton-max", (int?)403));
        issue.Cause.Should().Be(GitHubAccessCauses.InstallationRefused(403));
    }

    [Fact]
    public async Task GetAsync_ShouldNoteAnInvalidKey_WithoutCallingGitHub()
    {
        _rig.WriteKey("-----BEGIN RSA PRIVATE KEY-----\nbm90IGEga2V5\n-----END RSA PRIVATE KEY-----\n");

        (await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None)).Should().BeNull();

        _rig.GitHub.Calls.Should().BeEmpty();
        _rig.Monitor.Current().Should().ContainSingle().Which.Cause.Should().Be(GitHubAccessCauses.KeyInvalid);
    }

    [Fact]
    public async Task GetAsync_ShouldStayOff_WhenTheKeyIsMissing()
    {
        using var rig = new GitHubAppRig(writeKey: false);
        rig.Install("sulton-max", 11);

        rig.Tokens.IsConfigured.Should().BeFalse();
        (await rig.Tokens.GetAsync("sulton-max", CancellationToken.None)).Should().BeNull();

        rig.GitHub.Calls.Should().BeEmpty();
        rig.Monitor.Current().Should().ContainSingle().Which.Cause.Should().Be(GitHubAccessCauses.KeyMissing);
    }

    [Fact]
    public async Task GetAsync_ShouldStayOff_WhenNoAppIdIsSet()
    {
        using var rig = new GitHubAppRig(appId: "");

        (await rig.Tokens.GetAsync("sulton-max", CancellationToken.None)).Should().BeNull();

        rig.GitHub.Calls.Should().BeEmpty();
        rig.Monitor.Current().Should().ContainSingle().Which.Cause.Should().Be(GitHubAccessCauses.AppIdMissing);
    }

    [Fact]
    public async Task Invalidate_ShouldIssueANewTokenOnTheNextCall()
    {
        _rig.Install("sulton-max", 11);
        await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None);

        _rig.Tokens.Invalidate("Sulton-Max");

        (await _rig.Tokens.GetAsync("sulton-max", CancellationToken.None))!.Token.Should().Be("ghs_sulton-max_2");
    }
}
