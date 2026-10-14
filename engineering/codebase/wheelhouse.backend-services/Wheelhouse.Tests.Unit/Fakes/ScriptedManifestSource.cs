using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Answers manifest reads from a script and records what was asked. A branch nobody scripted cannot be read;
/// a commit with no scripted file has no manifest.</summary>
public sealed class ScriptedManifestSource : IProductManifestSource
{
    /// <summary>Gets the head of each branch, by <c>repository@branch</c>.</summary>
    public Dictionary<string, string> Heads { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the manifest file at each commit, by <c>repository@commit</c>.</summary>
    public Dictionary<string, ProductManifestFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets every head check, with whether it asked for a fresh read.</summary>
    public List<(string Repository, string Branch, bool Fresh)> HeadChecks { get; } = [];

    /// <summary>Gets every file read, as <c>repository@commit</c>.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>Gets or sets an exception every call throws, for a source that fails unexpectedly.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Moves a branch to a new commit that carries a manifest.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">The new head.</param>
    /// <param name="manifest">The manifest file text.</param>
    /// <param name="branch">The branch to move.</param>
    public void Push(string repository, string commit, string manifest, string branch = "main")
    {
        Heads[$"{repository}@{branch}"] = commit;
        Files[$"{repository}@{commit}"] = new ProductManifestFile(ProductManifestFileStatus.Found, System.Text.Encoding.UTF8.GetBytes(manifest));
    }

    /// <summary>Moves a branch to a new commit whose manifest file reads as the given status.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">The new head.</param>
    /// <param name="status">What reading the file gives at that commit.</param>
    /// <param name="branch">The branch to move.</param>
    public void Push(string repository, string commit, ProductManifestFileStatus status, string branch = "main")
    {
        Heads[$"{repository}@{branch}"] = commit;
        Files[$"{repository}@{commit}"] = new ProductManifestFile(status);
    }

    /// <inheritdoc />
    public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct)
    {
        HeadChecks.Add((repository, branch, fresh));
        if (Failure is not null)
            throw Failure;
        return Task.FromResult(Heads.GetValueOrDefault($"{repository}@{branch}"));
    }

    /// <inheritdoc />
    public Task<ProductManifestFile> ReadAsync(string repository, string commit, CancellationToken ct)
    {
        Reads.Add($"{repository}@{commit}");
        if (Failure is not null)
            throw Failure;
        return Task.FromResult(Files.GetValueOrDefault($"{repository}@{commit}") ?? new ProductManifestFile(ProductManifestFileStatus.Missing));
    }
}
