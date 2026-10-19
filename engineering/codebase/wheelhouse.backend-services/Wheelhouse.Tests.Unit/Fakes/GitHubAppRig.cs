using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Testing.Http;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Composes the GitHub App credential chain as the host does — over a generated RSA key in a temporary runner
/// root, a fake GitHub and a fake clock — so a test can script GitHub and move time.</summary>
public sealed class GitHubAppRig : IDisposable
{
    /// <summary>Holds the App ID the rig configures.</summary>
    public const string AppId = "123456";

    /// <summary>Builds the rig with the App configured and its key in place.</summary>
    /// <param name="appId">The App ID; empty leaves the App off.</param>
    /// <param name="writeKey">Whether the key file is placed.</param>
    public GitHubAppRig(string appId = AppId, bool writeKey = true)
    {
        Root = Directory.CreateTempSubdirectory("wheelhouse-github-").FullName;
        Settings = new DeploymentSettings
        {
            Root = Root,
            GitHubAppId = appId,
            GitHubAppKeyFile = Path.Combine(Root, "github-app", "private-key.pem"),
            GitHubTokenFile = Path.Combine(Root, "token"),
        };
        if (writeKey)
            WriteKey(Key.ExportRSAPrivateKeyPem());
        GitHub.Answers["GET app/installations"] = () => FakeGitHubApi.Json(HttpStatusCode.OK, "[]");
        Monitor = new GitHubAccessMonitor(Settings, Clock);
        Tokens = new GitHubAppTokens(
            new SingleClientHttpClientFactory(GitHub, new Uri("https://api.github.com/")), Settings, Clock, Monitor,
            NullLogger<GitHubAppTokens>.Instance);
        Credentials = new GitHubCredentials(Tokens, Settings);
    }

    private readonly List<(string Owner, long Id)> _installations = [];

    /// <summary>Gets the App's key; its public half verifies what the rig signed.</summary>
    public RSA Key { get; } = RSA.Create(2048);

    /// <summary>Gets the temporary runner root.</summary>
    public string Root { get; }

    /// <summary>Gets the settings the chain runs with.</summary>
    public DeploymentSettings Settings { get; }

    /// <summary>Gets the clock, starting at a fixed instant.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Gets the scripted GitHub.</summary>
    public FakeGitHubApi GitHub { get; } = new();

    /// <summary>Gets the access monitor.</summary>
    public GitHubAccessMonitor Monitor { get; }

    /// <summary>Gets the installation tokens under test.</summary>
    public GitHubAppTokens Tokens { get; }

    /// <summary>Gets the credential chain under test.</summary>
    public GitHubCredentials Credentials { get; }

    /// <summary>Writes the key file.</summary>
    /// <param name="pem">The file's text.</param>
    public void WriteKey(string pem)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings.GitHubAppKeyFile)!);
        File.WriteAllText(Settings.GitHubAppKeyFile, pem);
    }

    /// <summary>Scripts an installation for an owner — listed by <c>/app/installations</c> — and the tokens it issues.</summary>
    /// <param name="owner">The owner, a user or an organization.</param>
    /// <param name="installation">The installation id.</param>
    /// <param name="lifetime">How long each issued token lives; an hour by default.</param>
    public void Install(string owner, long installation, TimeSpan? lifetime = null)
    {
        _installations.Add((owner, installation));
        var listing = string.Join(",", _installations.Select(item => $$$"""{"id":{{{item.Id}}},"account":{"login":"{{{item.Owner}}}"}}"""));
        GitHub.Answers["GET app/installations"] = () => FakeGitHubApi.Json(HttpStatusCode.OK, "[" + listing + "]");
        var issued = 0;
        GitHub.Answers[$"POST app/installations/{installation}/access_tokens"] =
            () => FakeGitHubApi.Token($"ghs_{owner}_{++issued}", Clock.GetUtcNow() + (lifetime ?? TimeSpan.FromHours(1)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Key.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
