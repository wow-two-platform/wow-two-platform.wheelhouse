namespace Wheelhouse.Application.Abstractions;

/// <summary>Reads a product's manifest file from its source repository, one commit at a time. A commit never changes, so
/// a source may keep what it read for a commit as long as it likes; only a branch's head moves.</summary>
public interface IProductManifestSource
{
    /// <summary>Gets the full SHA of the branch's newest commit. A source may answer from a head it read a few minutes
    /// ago; <paramref name="fresh"/> asks it to read the head again now.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to read.</param>
    /// <param name="fresh">Whether to skip a remembered head.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The commit, or <c>null</c> when the branch cannot be read.</returns>
    Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct);

    /// <summary>Reads the manifest file as it was at a commit.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="commit">A full commit SHA <see cref="HeadAsync"/> returned.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<ProductManifestFile> ReadAsync(string repository, string commit, CancellationToken ct);
}
