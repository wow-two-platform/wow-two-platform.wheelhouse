using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.GitHub;

/// <summary>Tests for <see cref="GitHubCredentials"/> and <see cref="GitHubCredentialHandler"/>: the order a call's
/// credential is chosen in — the App's token for the owner, the token file, the caller's own, anonymous — and the
/// refusals each call notes.</summary>
public sealed class GitHubCredentialsTests : IDisposable
{
    private readonly GitHubAppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task ForOwnerAsync_ShouldPreferTheAppsTokenForTheOwner()
    {
        _rig.Install("ocharo-hq", 22, organization: true);
        File.WriteAllText(_rig.Settings.GitHubTokenFile, "FALLBACK\n");

        var credential = await _rig.Credentials.ForOwnerAsync("ocharo-hq", CancellationToken.None);

        credential.Should().Be(new GitHubCredential("ghs_ocharo-hq_1", GitHubCredentialSource.App));
        credential!.ToString().Should().NotContain("ghs_", "a credential never prints its token");
    }

    [Fact]
    public async Task ForOwnerAsync_ShouldFallBackToTheTokenFile_ForAnOwnerWithoutTheApp()
    {
        File.WriteAllText(_rig.Settings.GitHubTokenFile, "FALLBACK\n");

        var credential = await _rig.Credentials.ForOwnerAsync("wow-two", CancellationToken.None);

        credential.Should().Be(new GitHubCredential("FALLBACK", GitHubCredentialSource.TokenFile));
    }

    [Fact]
    public async Task ForOwnerAsync_ShouldGiveNone_WithoutTheAppOrAUsableTokenFile()
    {
        File.WriteAllText(_rig.Settings.GitHubTokenFile, "two words\n");

        (await _rig.Credentials.ForOwnerAsync("wow-two", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Handler_ShouldAuthorizeEachCallForTheOwnerItsPathNames()
    {
        _rig.Install("ocharo-hq", 22, organization: true);
        _rig.GitHub.Answers["GET repos/ocharo-hq/ocharo-ws/commits/main"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _rig.GitHub.Answers["GET repos/wow-two/wow-two-ws/commits/main"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        using var client = Client();

        await client.SendAsync(Get("repos/Ocharo-HQ/ocharo-ws/commits/main", "SIGNED-IN"));
        await client.SendAsync(Get("repos/wow-two/wow-two-ws/commits/main", "SIGNED-IN"));
        await client.SendAsync(Get("repos/wow-two/wow-two-ws/commits/main"));

        var reads = _rig.GitHub.Calls.Where(call => call.Key.StartsWith("GET repos/", StringComparison.Ordinal)).ToList();
        reads.Select(call => call.Authorization).Should().Equal(
            "Bearer ghs_ocharo-hq_1", "Bearer SIGNED-IN", null);
    }

    [Fact]
    public async Task Handler_ShouldNoteARateLimitedOwner_AndClearItOnALaterSuccess()
    {
        var limited = true;
        _rig.GitHub.Answers["GET repos/wow-two/wow-two-ws/commits/main"] = () =>
        {
            if (!limited)
                return new HttpResponseMessage(HttpStatusCode.OK);
            var refused = new HttpResponseMessage(HttpStatusCode.Forbidden);
            refused.Headers.Add("X-RateLimit-Remaining", "0");
            return refused;
        };
        using var client = Client();

        await client.SendAsync(Get("repos/wow-two/wow-two-ws/commits/main"));

        var issue = _rig.Monitor.Current().Should().ContainSingle(item => item.Status == 403).Subject;
        issue.Owner.Should().Be("wow-two");
        issue.Cause.Should().Be(GitHubAccessCauses.Refused(403, authenticated: false, rateLimited: true));

        limited = false;
        await client.SendAsync(Get("repos/wow-two/wow-two-ws/commits/main"));
        _rig.Monitor.Current().Should().NotContain(item => item.Status == 403);
    }

    [Fact]
    public async Task Handler_ShouldDropARefusedAppToken_SoTheNextCallGetsANewOne()
    {
        _rig.Install("sulton-max", 11);
        _rig.GitHub.Answers["GET repos/sulton-max/10x-ventures/commits/main"] = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using var client = Client();

        await client.SendAsync(Get("repos/sulton-max/10x-ventures/commits/main"));
        await client.SendAsync(Get("repos/sulton-max/10x-ventures/commits/main"));

        _rig.GitHub.Calls.Where(call => call.Key.StartsWith("GET repos/", StringComparison.Ordinal))
            .Select(call => call.Authorization).Should().Equal("Bearer ghs_sulton-max_1", "Bearer ghs_sulton-max_2");
        _rig.Monitor.Current().Should().Contain(item => item.Owner == "sulton-max" && item.Status == 401);
    }

    [Fact]
    public async Task Handler_ShouldLeaveACallThatNamesNoOwnerAlone()
    {
        _rig.Install("sulton-max", 11);
        using var client = Client();

        await client.SendAsync(Get("rate_limit"));

        _rig.GitHub.Calls.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    private HttpClient Client() =>
        new(new GitHubCredentialHandler(_rig.Credentials, _rig.Tokens, _rig.Monitor) { InnerHandler = _rig.GitHub })
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };

    private static HttpRequestMessage Get(string path, string? signedIn = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (signedIn is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn);
        return request;
    }
}
