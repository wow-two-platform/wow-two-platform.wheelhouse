using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Services;

namespace Wheelhouse.Tests.Unit.Catalog;

/// <summary>Tests for <see cref="CatalogReleaseService"/>'s notes on a repository that yields no images: a product's only
/// repository leaves the product without images, while a repository of a product built from several (decision 70) adds no
/// services until it releases, and the primary one holds back every release set.</summary>
public sealed class CatalogReleaseServiceTests
{
    private const string Descriptor =
        """
        descriptor: 2
        product: ocharo
        services:
          desk:
            build: { dockerfile: deployment/Dockerfile }
            health: /health
        """;

    [Fact]
    public async Task DeriveAll_ShouldSayAFurtherRepositoryWithoutADescriptorAddsNoServicesYet()
    {
        var files = new Files { ["ocharo-hq/ocharo-platform"] = Descriptor, ["ocharo-hq/ocharo-marketing"] = null };

        var derived = await new CatalogReleaseService(files).DeriveAllAsync(
            "ocharo", ["ocharo-hq/ocharo-platform", "ocharo-hq/ocharo-marketing"], CancellationToken.None);

        derived[0].Derived.Note.Should().BeNull();
        derived[1].Derived.Release.Images.Should().BeEmpty();
        derived[1].Derived.Note.Should().Be(
            "ocharo-hq/ocharo-marketing@main has no engineering/deployment/deploy.yml yet, so it adds no services until it has one and releases.");
    }

    [Fact]
    public async Task DeriveAll_ShouldSayThePrimaryRepositoryHoldsBackEverySet()
    {
        var files = new Files { ["ocharo-hq/ocharo-platform"] = null, ["ocharo-hq/ocharo-studio"] = "descriptor: [" };

        var derived = await new CatalogReleaseService(files).DeriveAllAsync(
            "ocharo", ["ocharo-hq/ocharo-platform", "ocharo-hq/ocharo-studio", "ocharo-hq/ocharo-unread"], CancellationToken.None);

        derived[0].Derived.Note.Should().Be(
            "ocharo-hq/ocharo-platform@main has no engineering/deployment/deploy.yml yet; it serves the product's front door, so the product has no release sets until it has one and releases.");
        derived[1].Derived.Note.Should().StartWith("deploy.yml in ocharo-hq/ocharo-studio@main ")
            .And.EndWith(" Until it is fixed and releases, it adds no services.");
        derived[2].Derived.Note.Should().Be(
            "ocharo-hq/ocharo-unread@main could not be read from GitHub, so it adds no services until it can be read and releases.");
    }

    [Fact]
    public async Task Derive_ShouldKeepTheSingleRepositoryWording()
    {
        var files = new Files { ["owner/solo"] = null };

        var derived = await new CatalogReleaseService(files).DeriveAsync("solo", "owner/solo", CancellationToken.None);

        derived.Note.Should().Be("owner/solo@main has no engineering/deployment/deploy.yml, so the product has no images yet.");
        (await new CatalogReleaseService(files).DeriveAsync("solo", "owner/gone", CancellationToken.None)).Note
            .Should().Be("owner/gone@main could not be read from GitHub, so the product has no images yet.");
    }

    /// <summary>Each listed repository's <c>deploy.yml</c> at one commit; null for a branch without one. An unlisted
    /// repository's branch cannot be read.</summary>
    private sealed class Files : Dictionary<string, string?>, IRepositoryFileSource
    {
        public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct) =>
            Task.FromResult(ContainsKey(repository) ? new string('a', 40) : null);

        public Task<RepositoryFileRead> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct) =>
            Task.FromResult(this[repository] is { } text
                ? new RepositoryFileRead(RepositoryFileStatus.Found, Encoding.UTF8.GetBytes(text))
                : new RepositoryFileRead(RepositoryFileStatus.Missing));
    }
}
