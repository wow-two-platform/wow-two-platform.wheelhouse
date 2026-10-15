using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Infrastructure.Products;
using WoW.Two.Sdk.Backend.Beta.Caching.Core;
using WoW.Two.Sdk.Backend.Beta.Caching.Hybrid;
using WoW.Two.Sdk.Backend.Beta.Integrations;
using WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;
using WoW.Two.Sdk.Backend.Beta.Testing.Http;
using WoW.Two.Sdk.Backend.Beta.Testing.Integrations;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Composes <see cref="GitHubRepositoryReader"/> as the host does — the SDK GitHub client with the reader's
/// settings, and the SDK cache — over a scripted GitHub that records every request, so a test can tell what was asked
/// and how often.</summary>
public sealed class ScriptedGitHubReader : IDisposable
{
    private readonly ServiceProvider _services;

    /// <summary>Builds the reader over a fresh script and an empty cache.</summary>
    /// <param name="token">The operator token every call carries; <see langword="null"/> stands for no signed-in operator.</param>
    public ScriptedGitHubReader(string? token = "token")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCaching();
        services.AddSingleton<IAccessTokenService>(new FixedAccessTokenService(token));
        services.AddGitHubIntegration(GitHubRepositoryReader.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(() => GitHub);
        _services = services.BuildServiceProvider();

        Cache = _services.GetRequiredService<ICacheRepository>();
        Reader = new GitHubRepositoryReader(_services.GetRequiredService<IGitHubClient>(), Cache);
    }

    /// <summary>Gets the scripted GitHub REST API.</summary>
    public ScriptedHttpMessageHandler GitHub { get; } = new();

    /// <summary>Gets every request path and query, without the leading slash, in the order asked.</summary>
    public IReadOnlyList<string> Requests => [.. GitHub.Requests.Select(request => request.PathAndQuery)];

    /// <summary>Gets the cache the reader keeps heads, trees and files in.</summary>
    public ICacheRepository Cache { get; }

    /// <summary>Gets the reader under test.</summary>
    public GitHubRepositoryReader Reader { get; }

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();
}
