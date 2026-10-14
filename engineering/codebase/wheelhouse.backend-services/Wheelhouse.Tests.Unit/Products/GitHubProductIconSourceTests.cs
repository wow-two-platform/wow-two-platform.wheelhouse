using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Wheelhouse.Application.Products;
using Wheelhouse.Infrastructure.Products;
using WoW.Two.Sdk.Backend.Beta.Integrations;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="GitHubProductIconSource"/> over a scripted GitHub: it keeps only the files an icon can
/// come from, reads one file's bytes, refuses an oversized file, caches per commit and follows a moved branch.</summary>
public sealed class GitHubProductIconSourceTests
{
    private const string Repository = "owner/pilot";
    private const string Head = "repos/owner/pilot/commits/main";
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);
    private static readonly string Tree = $"repos/owner/pilot/git/trees/{First}?recursive=1";

    private readonly ScriptedGitHub _github = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly GitHubProductIconSource _source;

    public GitHubProductIconSourceTests()
    {
        _github.Raw[Head] = Encoding.ASCII.GetBytes(First);
        _source = new GitHubProductIconSource(
            new SingleClientFactory(_github), new FixedToken("token"), _cache, NullLogger<GitHubProductIconSource>.Instance);
    }

    private static string File(string path, string commit) => $"repos/owner/pilot/contents/{path}?ref={commit}";

    private static string TreeJson(params (string Path, long Size)[] blobs) =>
        "{\"tree\":[" + string.Join(',', blobs.Select(blob => $"{{\"type\":\"blob\",\"path\":\"{blob.Path}\",\"size\":{blob.Size}}}")) +
        ",{\"type\":\"tree\",\"path\":\"product\"}]}";

    [Fact]
    public async Task ListAsync_ShouldKeepOnlyTheFilesAnIconCanComeFrom_WhenTheTreeIsLarge()
    {
        _github.Json[Tree] = TreeJson(
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
        _github.Raw.Remove(Head);
        _github.Json[Tree] = TreeJson(("product/brand/icons/short-colored-dark.png", 4_000));

        var paths = await _source.ListAsync(Repository, "main", CancellationToken.None);

        paths.Should().BeEmpty();
        _github.Requests.Should().NotContain(request => request.Contains("git/trees"));
    }

    [Fact]
    public async Task ListAsync_ShouldListTheNewCommit_WhenTheBranchMovesOnAfterItsHeadExpires()
    {
        _github.Json[Tree] = TreeJson(("product/brand/icons/short-colored-light.png", 4_000));
        _github.Json[$"repos/owner/pilot/git/trees/{Second}?recursive=1"] =
            TreeJson(("product/brand/icons/short-colored-dark.png", 4_000));
        await _source.ListAsync(Repository, "main", CancellationToken.None);

        _github.Raw[Head] = Encoding.ASCII.GetBytes(Second);
        // The head entry lives HeadFor; removing it stands in for that expiry.
        _cache.Remove($"product-icon-head:{Repository}@main");
        var paths = await _source.ListAsync(Repository, "main", CancellationToken.None);

        paths.Should().BeEquivalentTo("product/brand/icons/short-colored-dark.png");
    }

    [Fact]
    public async Task ListAsync_ShouldAskGitHubOnce_WhenTheSameRepositoryIsListedAgain()
    {
        _github.Json[Tree] = TreeJson(("product/brand/icons/short-colored-dark.png", 4_000));

        await _source.ListAsync(Repository, "main", CancellationToken.None);
        await _source.ListAsync(Repository, "main", CancellationToken.None);

        _github.Requests.Count(request => request.Contains("git/trees")).Should().Be(1);
        _github.Requests.Count(request => request == Head).Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTheBytesAndMediaType_WhenTheFileExists()
    {
        _github.Raw[File("product/brand/icons/short-colored-dark.svg", First)] = "<svg/>"u8.ToArray();

        var icon = await _source.ReadAsync(Repository, "main", "product/brand/icons/short-colored-dark.svg", CancellationToken.None);

        icon.Should().NotBeNull();
        icon!.ContentType.Should().Be("image/svg+xml");
        icon.Path.Should().Be("product/brand/icons/short-colored-dark.svg");
        icon.Content.Should().Equal("<svg/>"u8.ToArray());
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnNothing_WhenTheFileExceedsTheLimit()
    {
        _github.Raw[File("product/brand/icons/full-colored-light.png", First)] = new byte[ProductIconPaths.MaxBytes + 1];

        var icon = await _source.ReadAsync(Repository, "main", "product/brand/icons/full-colored-light.png", CancellationToken.None);

        icon.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnNothing_WhenTheFileIsMissing()
    {
        var icon = await _source.ReadAsync(Repository, "main", "product/brand/icons/short-colored-dark.png", CancellationToken.None);

        icon.Should().BeNull();
    }

    private sealed class FixedToken(string token) : IAccessTokenService
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(token);
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };
    }

    private sealed class ScriptedGitHub : HttpMessageHandler
    {
        public Dictionary<string, string> Json { get; } = [];

        public Dictionary<string, byte[]> Raw { get; } = [];

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(key);
            if (Json.TryGetValue(key, out var json))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(Raw.TryGetValue(key, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
