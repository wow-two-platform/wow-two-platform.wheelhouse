using System.Text;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Answers repository file reads from a script and records what was asked. A branch nobody scripted cannot be
/// read; a file a commit was not given is missing at that commit.</summary>
public sealed class ScriptedRepositoryFiles : IRepositoryFileSource
{
    /// <summary>Gets the head of each branch, by <c>repository@branch</c>.</summary>
    public Dictionary<string, string> Heads { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets each file at each commit, by <c>repository@commit:path</c>.</summary>
    public Dictionary<string, RepositoryFileRead> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets every head check, with whether it asked for a fresh read.</summary>
    public List<(string Repository, string Branch, bool Fresh)> HeadChecks { get; } = [];

    /// <summary>Gets every file read, as <c>repository@commit:path</c>.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>Gets or sets an exception every call throws, for a source that fails unexpectedly.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Moves a branch to a new commit that carries one file with the given text.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">The new head.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="text">The file's text.</param>
    /// <param name="branch">The branch to move.</param>
    public void Push(string repository, string commit, string path, string text, string branch = "main") =>
        Push(repository, commit, path, new RepositoryFileRead(RepositoryFileStatus.Found, Encoding.UTF8.GetBytes(text)), branch);

    /// <summary>Moves a branch to a new commit whose file reads as the given status.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">The new head.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="status">What reading the file gives at that commit.</param>
    /// <param name="branch">The branch to move.</param>
    public void Push(string repository, string commit, string path, RepositoryFileStatus status, string branch = "main") =>
        Push(repository, commit, path, new RepositoryFileRead(status), branch);

    /// <inheritdoc />
    public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct)
    {
        HeadChecks.Add((repository, branch, fresh));
        if (Failure is not null)
            throw Failure;
        return Task.FromResult(Heads.GetValueOrDefault($"{repository}@{branch}"));
    }

    /// <inheritdoc />
    public Task<RepositoryFileRead> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct)
    {
        Reads.Add($"{repository}@{commit}:{path}");
        if (Failure is not null)
            throw Failure;
        return Task.FromResult(Files.GetValueOrDefault($"{repository}@{commit}:{path}") ?? new RepositoryFileRead(RepositoryFileStatus.Missing));
    }

    private void Push(string repository, string commit, string path, RepositoryFileRead file, string branch)
    {
        Heads[$"{repository}@{branch}"] = commit;
        Files[$"{repository}@{commit}:{path}"] = file;
    }
}
