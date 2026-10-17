using System.Text;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Serves repository files from a dictionary of commits, so no test reads GitHub. A test pushes files to a
/// repository's branch the way a developer would: each push is a new commit that keeps the branch's other files. The
/// stub keeps no head for a few minutes (the real reader does), so every read sees the newest push.</summary>
public sealed class StubRepositoryFileSource : IRepositoryFileSource
{
    private readonly Dictionary<string, string> _heads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, RepositoryFileRead>> _commits = new(StringComparer.OrdinalIgnoreCase);
    private int _count;

    /// <summary>Every file read the host made, as <c>repository@commit:path</c>.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>Pushes a commit that adds or replaces one file, keeping the branch's others, and makes it the head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="text">The file's text.</param>
    /// <param name="branch">The branch to move.</param>
    /// <returns>The new commit's SHA.</returns>
    public string Push(string repository, string path, string text, string branch = "main") =>
        Push(repository, path, new RepositoryFileRead(RepositoryFileStatus.Found, Encoding.UTF8.GetBytes(text)), branch);

    /// <summary>Pushes a commit whose file reads as the given status (too large, unreadable) and makes it the head.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="path">The file's path.</param>
    /// <param name="status">What reading the file gives at that commit.</param>
    /// <param name="branch">The branch to move.</param>
    /// <returns>The new commit's SHA.</returns>
    public string Push(string repository, string path, RepositoryFileStatus status, string branch = "main") =>
        Push(repository, path, new RepositoryFileRead(status), branch);

    /// <summary>Pushes a commit with no files, so the branch is readable but carries nothing.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to move.</param>
    /// <returns>The new commit's SHA.</returns>
    public string PushEmpty(string repository, string branch = "main")
    {
        var commit = (++_count).ToString("x40");
        _commits[commit] = [];
        _heads[$"{repository}@{branch}"] = commit;
        return commit;
    }

    /// <summary>Forgets every commit and recorded read.</summary>
    public void Clear()
    {
        _heads.Clear();
        _commits.Clear();
        Reads.Clear();
    }

    /// <inheritdoc />
    public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct) =>
        Task.FromResult(_heads.GetValueOrDefault($"{repository}@{branch}"));

    /// <inheritdoc />
    public Task<RepositoryFileRead> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct)
    {
        Reads.Add($"{repository}@{commit}:{path}");
        var file = _commits.GetValueOrDefault(commit)?.GetValueOrDefault(path) ?? new RepositoryFileRead(RepositoryFileStatus.Missing);
        return Task.FromResult(file is { Status: RepositoryFileStatus.Found, Content.Length: var length } && length > maxBytes
            ? new RepositoryFileRead(RepositoryFileStatus.TooLarge)
            : file);
    }

    private string Push(string repository, string path, RepositoryFileRead file, string branch)
    {
        var files = _heads.TryGetValue($"{repository}@{branch}", out var head)
            ? new Dictionary<string, RepositoryFileRead>(_commits[head])
            : [];
        files[path] = file;
        var commit = (++_count).ToString("x40");
        _commits[commit] = files;
        _heads[$"{repository}@{branch}"] = commit;
        return commit;
    }
}
