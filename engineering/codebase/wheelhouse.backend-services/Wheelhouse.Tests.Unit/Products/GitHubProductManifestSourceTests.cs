using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;
using Wheelhouse.Infrastructure.Products;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="GitHubProductManifestSource"/> over a scripted GitHub: the fixed manifest path read at a
/// commit, a commit's verdict kept for good, no verdict retried, and the branch head kept only a few minutes.</summary>
public sealed class GitHubProductManifestSourceTests
{
    private const string Repository = "owner/pilot";
    private const string Head = "repos/owner/pilot/commits/main";
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);

    private readonly ScriptedGitHub _github = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly GitHubProductManifestSource _source;

    public GitHubProductManifestSourceTests()
    {
        _github.Raw[Head] = Encoding.ASCII.GetBytes(First);
        _source = new GitHubProductManifestSource(new GitHubRepositoryReader(
            new SingleClientFactory(_github), new FixedToken("token"), _cache, NullLogger<GitHubRepositoryReader>.Instance));
    }

    private static string Manifest(string commit) => $"repos/owner/pilot/contents/product/product.json?ref={commit}";

    [Fact]
    public async Task ReadAsync_ShouldReturnTheFileAtTheCommit_WhenTheCommitCarriesIt()
    {
        _github.Raw[Manifest(First)] = "{ }"u8.ToArray();

        var file = await _source.ReadAsync(Repository, First, CancellationToken.None);

        file.Status.Should().Be(ProductManifestFileStatus.Found);
        file.Content.Should().Equal("{ }"u8.ToArray());
        _github.Requests.Should().Equal(Manifest(First));
    }

    [Fact]
    public async Task ReadAsync_ShouldSendTheOperatorsToken_WhenItReadsTheFile()
    {
        _github.Raw[Manifest(First)] = "{ }"u8.ToArray();

        await _source.ReadAsync(Repository, First, CancellationToken.None);
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        _github.Authorizations.Should().OnlyContain(header => header == "Bearer token");
    }

    [Fact]
    public async Task ReadAsync_ShouldAskOnceAndRememberMissing_WhenTheCommitHasNoManifest()
    {
        var first = await _source.ReadAsync(Repository, First, CancellationToken.None);
        var again = await _source.ReadAsync(Repository, First, CancellationToken.None);

        (first.Status, again.Status).Should().Be((ProductManifestFileStatus.Missing, ProductManifestFileStatus.Missing));
        _github.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAgainForANewCommit_WhenTheBranchMovedOn()
    {
        _github.Raw[Manifest(Second)] = "{ }"u8.ToArray();
        await _source.ReadAsync(Repository, First, CancellationToken.None);

        var file = await _source.ReadAsync(Repository, Second, CancellationToken.None);

        file.Status.Should().Be(ProductManifestFileStatus.Found);
        _github.Requests.Should().Equal(Manifest(First), Manifest(Second));
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTooLargeAndRememberIt_WhenTheFileExceedsTheLimit()
    {
        _github.Raw[Manifest(First)] = new byte[ProductManifestParser.MaxBytes + 1];

        var first = await _source.ReadAsync(Repository, First, CancellationToken.None);
        await _source.ReadAsync(Repository, First, CancellationToken.None);

        first.Status.Should().Be(ProductManifestFileStatus.TooLarge);
        first.Content.Should().BeNull();
        _github.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAFileAtTheLimit_WhenItIsExactlyTheLimit()
    {
        _github.Raw[Manifest(First)] = new byte[ProductManifestParser.MaxBytes];

        (await _source.ReadAsync(Repository, First, CancellationToken.None)).Status.Should().Be(ProductManifestFileStatus.Found);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReadAsync_ShouldReturnUnreadableAndAskAgain_WhenGitHubGivesNoVerdict(HttpStatusCode status)
    {
        _github.Failures[Manifest(First)] = status;

        var first = await _source.ReadAsync(Repository, First, CancellationToken.None);
        _github.Failures.Clear();
        _github.Raw[Manifest(First)] = "{ }"u8.ToArray();
        var again = await _source.ReadAsync(Repository, First, CancellationToken.None);

        (first.Status, again.Status).Should().Be((ProductManifestFileStatus.Unreadable, ProductManifestFileStatus.Found));
    }

    [Fact]
    public async Task HeadAsync_ShouldAskGitHubOnce_WhenTheBranchIsReadAgainWithinTheWindow()
    {
        var first = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        _github.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        var again = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (first, again).Should().Be((First, First));
        _github.Requests.Should().Equal(Head);
    }

    [Fact]
    public async Task HeadAsync_ShouldReadTheMovedBranch_WhenTheKeptHeadHasExpired()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        _github.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        // The head entry lives HeadFor (five minutes); removing it stands in for that expiry.
        _cache.Remove($"github-head:{Repository}@main");

        (await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(Second);
        GitHubRepositoryReader.HeadFor.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task HeadAsync_ShouldSkipTheKeptHeadAndKeepTheNewOne_WhenAFreshReadIsAsked()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        _github.Raw[Head] = Encoding.ASCII.GetBytes(Second);

        var fresh = await _source.HeadAsync(Repository, "main", fresh: true, CancellationToken.None);
        var kept = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (fresh, kept).Should().Be((Second, Second));
        _github.Requests.Should().Equal(Head, Head);
    }

    [Fact]
    public async Task HeadAsync_ShouldLeaveTheKeptHeadAlone_WhenAFreshReadFails()
    {
        await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        _github.Failures[Head] = HttpStatusCode.BadGateway;

        var fresh = await _source.HeadAsync(Repository, "main", fresh: true, CancellationToken.None);
        var kept = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (fresh, kept).Should().Be((null, First));
    }

    [Fact]
    public async Task HeadAsync_ShouldRememberAnUnreadableBranch_WhenItIsReadAgainWithinTheWindow()
    {
        _github.Raw.Remove(Head);

        var first = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        var again = await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);

        (first, again).Should().Be((null, null));
        _github.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task HeadAsync_ShouldReturnNothing_WhenGitHubAnswersSomethingThatIsNoCommit()
    {
        _github.Raw[Head] = "<html>rate limited</html>"u8.ToArray();

        (await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task HeadAsync_ShouldKeepEachBranchApart_WhenTwoBranchesAreRead()
    {
        _github.Raw["repos/owner/pilot/commits/feat%2Flogin"] = Encoding.ASCII.GetBytes(Second);

        (await _source.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(First);
        (await _source.HeadAsync(Repository, "feat/login", fresh: false, CancellationToken.None)).Should().Be(Second);
    }
}
