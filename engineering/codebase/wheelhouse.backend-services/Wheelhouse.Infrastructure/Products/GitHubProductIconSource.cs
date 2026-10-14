using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Reads a product's icon files from its GitHub repository through the shared <see cref="GitHubRepositoryReader"/>,
/// so a pushed icon shows soon after and a commit's files are read once.</summary>
/// <remarks>Which file is chosen is the icon convention's decision, made by the caller; this source lists the files an
/// icon can come from and reads the chosen one.</remarks>
public sealed class GitHubProductIconSource(GitHubRepositoryReader reader) : IProductIconSource
{
    private const string PathsPurpose = "product-icon-paths";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string repository, string branch, CancellationToken ct)
    {
        var commit = await reader.HeadAsync(repository, branch, fresh: false, ct);
        if (commit is null)
            return [];

        return await reader.ListAsync(
                repository, commit, PathsPurpose,
                (path, size) => (size is null || size <= ProductIconPaths.MaxBytes) && ProductIconPaths.IsCandidate(path), ct)
            ?? [];
    }

    /// <inheritdoc />
    public async Task<ProductIconImage?> ReadAsync(string repository, string branch, string path, CancellationToken ct)
    {
        var commit = await reader.HeadAsync(repository, branch, fresh: false, ct);
        if (commit is null)
            return null;

        var file = await reader.ReadAsync(repository, commit, path, ProductIconPaths.MaxBytes, ct);
        return file is { Status: RepositoryFileStatus.Found, Content: { } bytes }
            ? new ProductIconImage(bytes, ProductIconPaths.ContentType(path), path)
            : null;
    }
}
