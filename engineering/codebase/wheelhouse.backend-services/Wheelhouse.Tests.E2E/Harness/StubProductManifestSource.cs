using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Serves product manifests from a dictionary of commits, so no test reads GitHub. A test pushes a commit to a
/// repository's branch with the manifest text it carries, the way a developer would; the stub keeps no head for a few
/// minutes (the real reader does, and its own tests cover that), so every read sees the newest push.</summary>
public sealed class StubProductManifestSource : IProductManifestSource
{
    private readonly Dictionary<string, string> _heads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProductManifestFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private int _commits;

    /// <summary>The repository, branch and freshness of every head check the host made.</summary>
    public List<(string Repository, string Branch, bool Fresh)> HeadChecks { get; } = [];

    /// <summary>Every manifest read the host made, as <c>repository@commit</c>.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>Pushes a commit that carries a manifest and makes it the branch's head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="manifest">The manifest file text.</param>
    /// <param name="branch">The branch to move.</param>
    /// <returns>The new commit's SHA.</returns>
    public string Push(string repository, string manifest, string branch = "main") =>
        Push(repository, new ProductManifestFile(ProductManifestFileStatus.Found, System.Text.Encoding.UTF8.GetBytes(manifest)), branch);

    /// <summary>Pushes a commit whose manifest file reads as the given status (no file, too large) and makes it the head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="status">What reading the file gives at that commit.</param>
    /// <param name="branch">The branch to move.</param>
    /// <returns>The new commit's SHA.</returns>
    public string Push(string repository, ProductManifestFileStatus status, string branch = "main") =>
        Push(repository, new ProductManifestFile(status), branch);

    /// <summary>Makes a repository's branch unreadable, as when GitHub is down or refuses the token.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch.</param>
    public void Unreachable(string repository, string branch = "main") => _heads.Remove($"{repository}@{branch}");

    /// <summary>Forgets every commit and recorded call.</summary>
    public void Clear()
    {
        _heads.Clear();
        _files.Clear();
        HeadChecks.Clear();
        Reads.Clear();
    }

    /// <inheritdoc />
    public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct)
    {
        HeadChecks.Add((repository, branch, fresh));
        return Task.FromResult(_heads.GetValueOrDefault($"{repository}@{branch}"));
    }

    /// <inheritdoc />
    public Task<ProductManifestFile> ReadAsync(string repository, string commit, CancellationToken ct)
    {
        Reads.Add($"{repository}@{commit}");
        return Task.FromResult(_files.GetValueOrDefault($"{repository}@{commit}") ?? new ProductManifestFile(ProductManifestFileStatus.Missing));
    }

    private string Push(string repository, ProductManifestFile file, string branch)
    {
        var commit = (++_commits).ToString("x40");
        _heads[$"{repository}@{branch}"] = commit;
        _files[$"{repository}@{commit}"] = file;
        return commit;
    }
}
