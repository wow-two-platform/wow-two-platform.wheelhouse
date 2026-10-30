using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Infrastructure.GitHub;
using WoW.Two.Sdk.Beta.Be.Caching.Core;
using WoW.Two.Sdk.Beta.Be.Caching.Hybrid;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Integrations;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Composes the SDK <see cref="GitHubRepositoryReader"/> as the host does — Wheelhouse's head and commit
/// lifetimes over the SDK cache — on the SDK <see cref="FakeGitHubClient"/>, so a test pushes commits to several
/// repositories and reads them through the real per-commit cache. A repository nobody pushed cannot be read.</summary>
public sealed class FakeGitHubReads : IDisposable
{
    private readonly ServiceProvider _services;

    /// <summary>Builds the reader over an empty fake and an empty cache.</summary>
    public FakeGitHubReads()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCaching();
        _services = services.BuildServiceProvider();

        Cache = _services.GetRequiredService<ICacheRepository>();
        var options = new GitHubRepositoryReaderOptions();
        GitHubReadSettings.ConfigureReader(options);
        Reader = new GitHubRepositoryReader(GitHub, Cache, options);
    }

    /// <summary>Gets the fake GitHub the reader reads through; it records every head, tree and file read.</summary>
    public FakeGitHubClient GitHub { get; } = new FakeGitHubClient().WithPushedOnly();

    /// <summary>Gets the cache the reader keeps heads, trees and files in.</summary>
    public ICacheRepository Cache { get; }

    /// <summary>Gets the reader under the service being tested.</summary>
    public GitHubRepositoryReader Reader { get; }

    /// <summary>Drops a branch's kept head, standing in for the <see cref="GitHubReadSettings.HeadFor"/> expiry, so the
    /// next read sees where the branch moved.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch.</param>
    public ValueTask ExpireHeadAsync(string repository, string branch = "main") =>
        Cache.RemoveAsync(GitHubRepositoryReader.HeadKey(repository, branch));

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();
}
