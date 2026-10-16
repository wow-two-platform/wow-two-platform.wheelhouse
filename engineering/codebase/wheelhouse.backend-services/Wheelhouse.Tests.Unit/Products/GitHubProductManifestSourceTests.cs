using System.Net;
using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;
using Wheelhouse.Infrastructure.Products;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Testing.Http;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="GitHubProductManifestSource"/> over a scripted GitHub: the fixed manifest path read at a
/// commit, a commit's verdict kept for good, no verdict retried, and the branch head kept only a few minutes.</summary>
public sealed class GitHubProductManifestSourceTests : IDisposable
{
    private const string Repository = "owner/pilot";
    private const string Head = "repos/owner/pilot/commits/main";
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);

    private readonly ScriptedGitHubReader _reads = new();
    private readonly GitHubProductManifestSource _source;

    public GitHubProductManifestSourceTests()
    {
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(First);
        _source = new GitHubProductManifestSource(_reads.Reader);
    }

    private ScriptedHttpMessageHandler GitHub => _reads.GitHub;

    public void Dispose() => _reads.Dispose();

    private static string Manifest(string commit) => $"repos/owner/pilot/contents/product/product.json?ref={commit}";

    [Fact]
    public async Task ReadAsync_ShouldReturnTheFileAtTheCommit_WhenTheCommitCarriesIt()
    {
        GitHub.Raw[Manifest(First)] = "{ }"u8.ToArray();

        var file = await _source.ReadAsync(Repository, First, CancellationToken.None);

        file.Status.Should().Be(ProductManifestFileStatus.Found);
        file.Content.Should().Equal("{ }"u8.ToArray());
        _reads.Requests.Should().Equal(Manifest(First));
    }

    [Fact]
    public async Task ReadAsync_ShouldSendTheOperatorsToken_WhenItReadsTheFile()
    {
        GitHub.Raw[Manifest(First)] = "{ }"u8.ToArray();

        await _source.ReadAsync(Repository, First, CancellationToken.None);
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        GitHub.Requests.Should().OnlyContain(request => request.Authorization == "Bearer token");
    }

    [Fact]
    public async Task ReadAsync_ShouldNameWheelhouseAndThePinnedApiVersion_WhenItCallsGitHub()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        var request = GitHub.Requests.Should().ContainSingle().Subject;
        request.Headers["User-Agent"].Should().Be("Wheelhouse");
        request.Headers["X-GitHub-Api-Version"].Should().Be("2022-11-28");
        request.Headers["Accept"].Should().Be("application/vnd.github.sha");
    }

    [Fact]
    public async Task HeadAsync_ShouldReadThePublicRepositoryAnonymously_WhenNoOperatorIsSignedIn()
    {
        using var anonymous = new ScriptedGitHubReader(token: null);
        anonymous.GitHub.Raw[Head] = Encoding.ASCII.GetBytes(First);
        var source = new GitHubProductManifestSource(anonymous.Reader);

        (await source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(First);
        anonymous.GitHub.Requests.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_ShouldAskOnceAndRememberMissing_WhenTheCommitHasNoManifest()
    {
        var first = await _source.ReadAsync(Repository, First, CancellationToken.None);
        var again = await _source.ReadAsync(Repository, First, CancellationToken.None);

        (first.Status, again.Status).Should().Be((ProductManifestFileStatus.Missing, ProductManifestFileStatus.Missing));
        _reads.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAgainForANewCommit_WhenTheBranchMovedOn()
    {
        GitHub.Raw[Manifest(Second)] = "{ }"u8.ToArray();
        await _source.ReadAsync(Repository, First, CancellationToken.None);

        var file = await _source.ReadAsync(Repository, Second, CancellationToken.None);

        file.Status.Should().Be(ProductManifestFileStatus.Found);
        _reads.Requests.Should().Equal(Manifest(First), Manifest(Second));
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTooLargeAndRememberIt_WhenTheFileExceedsTheLimit()
    {
        GitHub.Raw[Manifest(First)] = new byte[ProductManifestParser.MaxBytes + 1];

        var first = await _source.ReadAsync(Repository, First, CancellationToken.None);
        await _source.ReadAsync(Repository, First, CancellationToken.None);

        first.Status.Should().Be(ProductManifestFileStatus.TooLarge);
        first.Content.Should().BeNull();
        _reads.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAFileAtTheLimit_WhenItIsExactlyTheLimit()
    {
        GitHub.Raw[Manifest(First)] = new byte[ProductManifestParser.MaxBytes];

        (await _source.ReadAsync(Repository, First, CancellationToken.None)).Status.Should().Be(ProductManifestFileStatus.Found);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReadAsync_ShouldReturnUnreadableAndAskAgain_WhenGitHubGivesNoVerdict(HttpStatusCode status)
    {
        GitHub.Failures[Manifest(First)] = status;

        var first = await _source.ReadAsync(Repository, First, CancellationToken.None);
        GitHub.Failures.Clear();
        GitHub.Raw[Manifest(First)] = "{ }"u8.ToArray();
        var again = await _source.ReadAsync(Repository, First, CancellationToken.None);

        (first.Status, again.Status).Should().Be((ProductManifestFileStatus.Unreadable, ProductManifestFileStatus.Found));
    }

    [Fact]
    public async Task HeadAsync_ShouldAskGitHubOnce_WhenTheBranchIsReadAgainWithinTheWindow()
    {
        var first = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        var again = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (first, again).Should().Be((First, First));
        _reads.Requests.Should().Equal(Head);
    }

    [Fact]
    public async Task HeadAsync_ShouldReadTheMovedBranch_WhenTheKeptHeadHasExpired()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        // The head entry lives HeadFor (five minutes); removing it stands in for that expiry.
        await _reads.Cache.RemoveAsync(GitHubRepositoryReader.HeadKey(Repository, "main"));

        (await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(Second);
        GitHubRepositoryReader.HeadFor.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task HeadAsync_ShouldSkipTheKeptHeadAndKeepTheNewOne_WhenAFreshReadIsAsked()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);

        var fresh = await _source.HeadAsync(Repository, "main", fresh: true, CancellationToken.None);
        var kept = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (fresh, kept).Should().Be((Second, Second));
        _reads.Requests.Should().Equal(Head, Head);
    }

    [Fact]
    public async Task HeadAsync_ShouldLeaveTheKeptHeadAlone_WhenAFreshReadFails()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        GitHub.Failures[Head] = HttpStatusCode.BadGateway;

        var fresh = await _source.HeadAsync(Repository, "main", fresh: true, CancellationToken.None);
        var kept = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (fresh, kept).Should().Be((null, First));
    }

    [Fact]
    public async Task HeadAsync_ShouldRememberAnUnreadableBranch_WhenItIsReadAgainWithinTheWindow()
    {
        GitHub.Raw.Remove(Head);

        var first = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        var again = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (first, again).Should().Be((null, null));
        _reads.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task HeadAsync_ShouldReturnNothing_WhenGitHubAnswersSomethingThatIsNoCommit()
    {
        GitHub.Raw[Head] = "<html>rate limited</html>"u8.ToArray();

        (await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task HeadAsync_ShouldKeepEachBranchApart_WhenTwoBranchesAreRead()
    {
        GitHub.Raw["repos/owner/pilot/commits/feat%2Flogin"] = Encoding.ASCII.GetBytes(Second);

        (await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(First);
        (await _source.HeadAsync(Repository, "feat/login", fresh: false, CancellationToken.None)).Should().Be(Second);
    }
}
