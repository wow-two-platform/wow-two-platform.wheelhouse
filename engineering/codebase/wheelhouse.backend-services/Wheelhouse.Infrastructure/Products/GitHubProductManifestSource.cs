using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;
using WoW.Two.Sdk.Backend.Beta.Integrations.GitHub;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Reads a product's manifest file from its GitHub repository through the shared
/// <see cref="GitHubRepositoryReader"/>: the branch head, then <see cref="ProductManifestParser.Path"/> at a commit. The
/// path is fixed, so no tree is listed; a commit's verdict on the file is kept for good.</summary>
public sealed class GitHubProductManifestSource(GitHubRepositoryReader reader) : IProductManifestSource
{
    /// <inheritdoc />
    public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct) =>
        reader.HeadAsync(repository, branch, fresh, ct);

    /// <inheritdoc />
    public async Task<ProductManifestFile> ReadAsync(string repository, string commit, CancellationToken ct)
    {
        var file = await reader.ReadAsync(repository, commit, ProductManifestParser.Path, ProductManifestParser.MaxBytes, ct);
        return new ProductManifestFile(
            file.Outcome switch
            {
                FileRead.Found => ProductManifestFileStatus.Found,
                FileRead.NotFound => ProductManifestFileStatus.Missing,
                FileRead.TooLarge => ProductManifestFileStatus.TooLarge,
                _ => ProductManifestFileStatus.Unreadable,
            },
            file.Content);
    }
}
