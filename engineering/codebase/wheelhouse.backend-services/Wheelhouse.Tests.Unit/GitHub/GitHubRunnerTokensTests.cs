using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.GitHub;

/// <summary>Tests for <see cref="GitHubRunnerTokens"/>: the owner-only file of per-owner tokens the runner reads.</summary>
public sealed class GitHubRunnerTokensTests : IDisposable
{
    private readonly GitHubAppRig _rig = new();
    private readonly InMemoryProductsRepository _products = new();

    public GitHubRunnerTokensTests()
    {
        _products.Rows.AddRange(
        [
            Product("foreverpin", "sulton-max/10x-venture-forever-pin"),
            Product("pbn", "Sulton-Max/pbn-studio"),
            Product("ocharo", "ocharo-hq/ocharo-site"),
            Product("arcade", "wow-two-apps/arcade"),
        ]);
    }

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task PrepareAsync_ShouldWriteOneTokenPerInstalledOwner_ReadableByTheOwnerOnly()
    {
        _rig.Install("sulton-max", 11);
        _rig.Install("ocharo-hq", 22);

        var path = await Tokens().PrepareAsync(CancellationToken.None);

        path.Should().Be(GitHubRunnerFiles.TokensPath(_rig.Root));
        using var document = JsonDocument.Parse(File.ReadAllText(path!));
        var owners = document.RootElement.GetProperty("owners");
        owners.EnumerateObject().Select(owner => owner.Name).Should().Equal("ocharo-hq", "sulton-max");
        owners.GetProperty("sulton-max").GetProperty("token").GetString().Should().Be("ghs_sulton-max_1");
        owners.GetProperty("ocharo-hq").GetProperty("expiresAt").GetDateTimeOffset()
            .Should().Be(_rig.Clock.GetUtcNow().AddHours(1));
        document.RootElement.GetProperty("any").GetProperty("token").GetString()
            .Should().BeOneOf("ghs_ocharo-hq_1", "ghs_sulton-max_1", "wow-two-apps reads public repositories with a lent token");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(path!).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task PrepareAsync_ShouldLeaveTheFileAlone_WhileItsTokensStillServe()
    {
        _rig.Install("sulton-max", 11);
        var path = await Tokens().PrepareAsync(CancellationToken.None);
        var written = File.GetLastWriteTimeUtc(path!);
        File.SetLastWriteTimeUtc(path!, written.AddDays(-1));

        await Tokens().PrepareAsync(CancellationToken.None);

        File.GetLastWriteTimeUtc(path!).Should().Be(written.AddDays(-1));
    }

    [Fact]
    public async Task PrepareAsync_ShouldWriteNothing_WhenTheAppIsOff()
    {
        using var rig = new GitHubAppRig(appId: "");
        var tokens = new GitHubRunnerTokens(rig.Tokens, _products, rig.Settings, NullLogger<GitHubRunnerTokens>.Instance);

        (await tokens.PrepareAsync(CancellationToken.None)).Should().BeNull();

        File.Exists(GitHubRunnerFiles.TokensPath(rig.Root)).Should().BeFalse();
    }

    private GitHubRunnerTokens Tokens() =>
        new(_rig.Tokens, _products, _rig.Settings, NullLogger<GitHubRunnerTokens>.Instance);

    private static ProductEntity Product(string slug, string repository) => new()
    {
        Id = Guid.NewGuid(), Slug = slug, Name = slug, Description = "", Repository = repository, DefaultBranch = "main",
    };
}
