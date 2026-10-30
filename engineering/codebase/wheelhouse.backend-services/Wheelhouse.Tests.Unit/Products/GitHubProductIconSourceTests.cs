using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Products;
using Wheelhouse.Infrastructure.Products;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Http;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="GitHubProductIconSource"/> over a scripted GitHub: it keeps only the files an icon can
/// come from, reads one file's bytes, refuses an oversized file, caches per commit and follows a moved branch.</summary>
public sealed class GitHubProductIconSourceTests : IDisposable
{
    private const string Repository = "owner/pilot";
    private const string Head = "repos/owner/pilot/commits/main";
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);
    private static readonly string Tree = $"repos/owner/pilot/git/trees/{First}?recursive=1";

    private readonly ScriptedGitHubReader _reads = new();
    private readonly GitHubProductIconSource _source;

    public GitHubProductIconSourceTests()
    {
        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(First);
        _source = new GitHubProductIconSource(_reads.Reader);
    }

    private ScriptedHttpMessageHandler GitHub => _reads.GitHub;

    public void Dispose() => _reads.Dispose();

    private static string File(string path, string commit) => $"repos/owner/pilot/contents/{path}?ref={commit}";

    private static string TreeJson(params (string Path, long Size)[] blobs) =>
        "{\"tree\":[" + string.Join(',', blobs.Select(blob => $"{{\"type\":\"blob\",\"path\":\"{blob.Path}\",\"size\":{blob.Size}}}")) +
        ",{\"type\":\"tree\",\"path\":\"product\"}]}";

    [Fact]
    public async Task ListAsync_ShouldKeepOnlyTheFilesAnIconCanComeFrom_WhenTheTreeIsLarge()
    {
        GitHub.Json[Tree] = TreeJson(
            ("README.md", 120),
            ("src/Program.cs", 900),
            ("product/brand/icons/short-colored-dark.png", 4_000),
            ("product/brand/icons/services/redirect/full-colored-light.svg", 2_000),
            ("product/brand/icons/short-colored-light.png", ProductIconPaths.MaxBytes + 1),
            ("engineering/codebase/pilot.frontend-services/apps/web/public/favicon.svg", 700),
            ("docs/logo.png", 500));

        var paths = await _source.ListAsync(Repository, "main", CancellationToken.None);

        paths.Should().BeEquivalentTo(
            "product/brand/icons/short-colored-dark.png",
            "product/brand/icons/services/redirect/full-colored-light.svg",
            "engineering/codebase/pilot.frontend-services/apps/web/public/favicon.svg");
    }

    [Fact]
    public async Task ListAsync_ShouldReturnNothing_WhenTheRepositoryCannotBeRead()
    {
        var paths = await _source.ListAsync(Repository, "main", CancellationToken.None);

        paths.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnNothing_WhenTheBranchCannotBeRead()
    {
        GitHub.Raw.Remove(Head);
        GitHub.Json[Tree] = TreeJson(("product/brand/icons/short-colored-dark.png", 4_000));

        var paths = await _source.ListAsync(Repository, "main", CancellationToken.None);

        paths.Should().BeEmpty();
        _reads.Requests.Should().NotContain(request => request.Contains("git/trees"));
    }

    [Fact]
    public async Task ListAsync_ShouldListTheNewCommit_WhenTheBranchMovesOnAfterItsHeadExpires()
    {
        GitHub.Json[Tree] = TreeJson(("product/brand/icons/short-colored-light.png", 4_000));
        GitHub.Json[$"repos/owner/pilot/git/trees/{Second}?recursive=1"] =
            TreeJson(("product/brand/icons/short-colored-dark.png", 4_000));
        await _source.ListAsync(Repository, "main", CancellationToken.None);

        GitHub.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        // The head entry lives HeadFor; removing it stands in for that expiry.
        await _reads.Cache.RemoveAsync(GitHubRepositoryReader.HeadKey(Repository, "main"));
        var paths = await _source.ListAsync(Repository, "main", CancellationToken.None);

        paths.Should().BeEquivalentTo("product/brand/icons/short-colored-dark.png");
    }

    [Fact]
    public async Task ListAsync_ShouldAskGitHubOnce_WhenTheSameRepositoryIsListedAgain()
    {
        GitHub.Json[Tree] = TreeJson(("product/brand/icons/short-colored-dark.png", 4_000));

        await _source.ListAsync(Repository, "main", CancellationToken.None);
        await _source.ListAsync(Repository, "main", CancellationToken.None);

        _reads.Requests.Count(request => request.Contains("git/trees")).Should().Be(1);
        GitHub.CountOf(Head).Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTheBytesAndMediaType_WhenTheFileExists()
    {
        GitHub.Raw[File("product/brand/icons/short-colored-dark.svg", First)] = "<svg/>"u8.ToArray();

        var icon = await _source.ReadAsync(Repository, "main", "product/brand/icons/short-colored-dark.svg", CancellationToken.None);

        icon.Should().NotBeNull();
        icon!.ContentType.Should().Be("image/svg+xml");
        icon.Path.Should().Be("product/brand/icons/short-colored-dark.svg");
        icon.Content.Should().Equal("<svg/>"u8.ToArray());
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnNothing_WhenTheFileExceedsTheLimit()
    {
        GitHub.Raw[File("product/brand/icons/full-colored-light.png", First)] = new byte[ProductIconPaths.MaxBytes + 1];

        var icon = await _source.ReadAsync(Repository, "main", "product/brand/icons/full-colored-light.png", CancellationToken.None);

        icon.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnNothing_WhenTheFileIsMissing()
    {
        var icon = await _source.ReadAsync(Repository, "main", "product/brand/icons/short-colored-dark.png", CancellationToken.None);

        icon.Should().BeNull();
    }
}
