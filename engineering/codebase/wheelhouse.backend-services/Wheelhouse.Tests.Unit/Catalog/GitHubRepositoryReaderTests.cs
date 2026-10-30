using System.Net;
using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Catalog;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Http;

namespace Wheelhouse.Tests.Unit.Catalog;

/// <summary>Tests for the SDK <see cref="GitHubRepositoryReader"/> as Wheelhouse composes it, over a scripted GitHub: a file read at a commit, a
/// commit's verdict kept for good, no verdict retried, and the branch head kept only a few minutes. The descriptor,
/// which carries a product's identity, is the file read here.</summary>
public sealed class GitHubRepositoryReaderTests : IDisposable
{
    private const string Repository = "owner/pilot";
    private const string Head = "repos/owner/pilot/commits/main";
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);

    private readonly ScriptedGitHubReader _reads = new();
    private const int Limit = CatalogConstants.MaxDescriptorBytes;
    public GitHubRepositoryReaderTests() => GitHub.Raw[Head] = Encoding.ASCII.GetBytes(First);

    private ScriptedHttpMessageHandler GitHub => _reads.GitHub;

    public void Dispose() => _reads.Dispose();

    private static string File(string commit) => $"repos/owner/pilot/contents/engineering/deployment/deploy.yml?ref={commit}";

    private Task<RepositoryFile> ReadAsync(string commit) =>
        _reads.Reader.ReadAsync(Repository, commit, CatalogConstants.DescriptorPath, Limit, CancellationToken.None);

    [Fact]
    public async Task ReadAsync_ShouldReturnTheFileAtTheCommit_WhenTheCommitCarriesIt()
    {
        GitHub.Raw[File(First)] = "{ }"u8.ToArray();

        var file = await ReadAsync(First);

        file.Outcome.Should().Be(FileRead.Found);
        file.Content.Should().Equal("{ }"u8.ToArray());
        _reads.Requests.Should().Equal(File(First));
    }

    [Fact]
    public async Task ReadAsync_ShouldSendTheOwnersToken_WhenItReadsTheFile()
    {
        GitHub.Raw[File(First)] = "{ }"u8.ToArray();

        await ReadAsync(First);
        await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        GitHub.Requests.Should().OnlyContain(request => request.Authorization == "Bearer token");
    }

    [Fact]
    public async Task ReadAsync_ShouldNameWheelhouseAndThePinnedApiVersion_WhenItCallsGitHub()
    {
        await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        var request = GitHub.Requests.Should().ContainSingle().Subject;
        request.Headers["User-Agent"].Should().Be("Wheelhouse");
        request.Headers["X-GitHub-Api-Version"].Should().Be("2022-11-28");
        request.Headers["Accept"].Should().Be("application/vnd.github.sha");
    }

    [Fact]
    public async Task HeadAsync_ShouldReadThePublicRepositoryAnonymously_WhenNoOwnerIsSignedIn()
    {
        using var anonymous = new ScriptedGitHubReader(token: null);
        anonymous.GitHub.Raw[Head] = Encoding.ASCII.GetBytes(First);

        (await anonymous.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(First);
        anonymous.GitHub.Requests.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_ShouldAskOnceAndRememberMissing_WhenTheCommitHasNoFile()
    {
        var first = await ReadAsync(First);
        var again = await ReadAsync(First);

        (first.Outcome, again.Outcome).Should().Be((FileRead.NotFound, FileRead.NotFound));
        _reads.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAgainForANewCommit_WhenTheBranchMovedOn()
    {
        GitHub.Raw[File(Second)] = "{ }"u8.ToArray();
        await ReadAsync(First);

        var file = await ReadAsync(Second);

        file.Outcome.Should().Be(FileRead.Found);
        _reads.Requests.Should().Equal(File(First), File(Second));
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTooLargeAndRememberIt_WhenTheFileExceedsTheLimit()
    {
        GitHub.Raw[File(First)] = new byte[Limit + 1];

        var first = await ReadAsync(First);
        await ReadAsync(First);

        first.Outcome.Should().Be(FileRead.TooLarge);
        first.Content.Should().BeNull();
        _reads.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAFileAtTheLimit_WhenItIsExactlyTheLimit()
    {
        GitHub.Raw[File(First)] = new byte[Limit];

        (await ReadAsync(First)).Outcome.Should().Be(FileRead.Found);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReadAsync_ShouldGiveNoVerdictAndAskAgain_WhenGitHubGivesNoVerdict(HttpStatusCode status)
    {
        GitHub.Failures[File(First)] = status;

        var first = await ReadAsync(First);
        GitHub.Failures.Clear();
        GitHub.Raw[File(First)] = "{ }"u8.ToArray();
        var again = await ReadAsync(First);

        first.Outcome.Should().BeOneOf(FileRead.Unauthorized, FileRead.Failed);
        again.Outcome.Should().Be(FileRead.Found);
    }

    [Fact]
    public async Task HeadAsync_ShouldAskGitHubOnce_WhenTheBranchIsReadAgainWithinTheWindow()
    {
        var first = await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        var again = await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (first, again).Should().Be((First, First));
        _reads.Requests.Should().Equal(Head);
    }

    [Fact]
    public async Task HeadAsync_ShouldReadTheMovedBranch_WhenTheKeptHeadHasExpired()
    {
        await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        // The head entry lives HeadFor (five minutes); removing it stands in for that expiry.
        await _reads.Cache.RemoveAsync(GitHubRepositoryReader.HeadKey(Repository, "main"));

        (await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(Second);
        GitHubReadSettings.HeadFor.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task HeadAsync_ShouldSkipTheKeptHeadAndKeepTheNewOne_WhenAFreshReadIsAsked()
    {
        await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);

        var fresh = await _reads.Reader.HeadAsync(Repository, "main", fresh: true, CancellationToken.None);
        var kept = await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (fresh, kept).Should().Be((Second, Second));
        _reads.Requests.Should().Equal(Head, Head);
    }

    [Fact]
    public async Task HeadAsync_ShouldLeaveTheKeptHeadAlone_WhenAFreshReadFails()
    {
        await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Failures[Head] = HttpStatusCode.BadGateway;

        var fresh = await _reads.Reader.HeadAsync(Repository, "main", fresh: true, CancellationToken.None);
        var kept = await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (fresh, kept).Should().Be((null, First));
    }

    [Fact]
    public async Task HeadAsync_ShouldRememberAnUnreadableBranch_WhenItIsReadAgainWithinTheWindow()
    {
        GitHub.Raw.Remove(Head);

        var first = await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        var again = await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (first, again).Should().Be((null, null));
        _reads.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task HeadAsync_ShouldReturnNothing_WhenGitHubAnswersSomethingThatIsNoCommit()
    {
        GitHub.Raw[Head] = "<html>rate limited</html>"u8.ToArray();

        (await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task HeadAsync_ShouldKeepEachBranchApart_WhenTwoBranchesAreRead()
    {
        GitHub.Raw["repos/owner/pilot/commits/feat%2Flogin"] = Encoding.ASCII.GetBytes(Second);

        (await _reads.Reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(First);
        (await _reads.Reader.HeadAsync(Repository, "feat/login", fresh: false, CancellationToken.None)).Should().Be(Second);
    }
}
