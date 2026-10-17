namespace Wheelhouse.Application.Abstractions;

/// <summary>Reads files from GitHub repositories, one commit at a time: the wow2 catalog and each product's
/// <c>deploy.yml</c>. A commit never changes, so a source may keep what it read for a commit; only a branch head moves.</summary>
public interface IRepositoryFileSource
{
    /// <summary>Gets the full SHA of a branch's newest commit.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch, or any ref GitHub resolves to a commit.</param>
    /// <param name="fresh">Whether to skip a head the source read a few minutes ago.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The commit, or <c>null</c> when the branch cannot be read.</returns>
    Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct);

    /// <summary>Reads one file as it was at a commit, at most <paramref name="maxBytes"/> of it.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="maxBytes">The largest file to read.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>What the read gave.</returns>
    Task<RepositoryFileRead> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct);
}
