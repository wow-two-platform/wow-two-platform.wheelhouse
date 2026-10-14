namespace Wheelhouse.Application.Abstractions;

/// <summary>A product's own icon, found in its source repository.</summary>
/// <param name="Content">The image bytes.</param>
/// <param name="ContentType">The image media type.</param>
/// <param name="Path">The repository path the icon was read from.</param>
public sealed record ProductIconImage(byte[] Content, string ContentType, string Path);

/// <summary>Reads the icon files a product's source repository carries, so every product shows its own mark.</summary>
public interface IProductIconSource
{
    /// <summary>Lists the repository's files an icon can come from; empty when it carries none or cannot be read.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to read.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<IReadOnlyList<string>> ListAsync(string repository, string branch, CancellationToken ct);

    /// <summary>Reads one listed file, or <see langword="null"/> when it is missing, too large or cannot be read.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="branch">The branch to read.</param>
    /// <param name="path">A path <see cref="ListAsync"/> returned.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<ProductIconImage?> ReadAsync(string repository, string branch, string path, CancellationToken ct);
}
