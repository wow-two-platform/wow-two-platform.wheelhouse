using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Serves repository files from a dictionary keyed by repository, so no test reads GitHub. Like the real
/// source it lists only the files an icon can come from; which one wins is the host's decision.</summary>
public sealed class StubProductIconSource : IProductIconSource
{
    private readonly Dictionary<string, Dictionary<string, ProductIconImage>> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The repository and branch of every listing the host asked for.</summary>
    public List<(string Repository, string Branch)> Listings { get; } = [];

    /// <summary>Puts a file into a repository.</summary>
    /// <param name="repository">The <c>{owner}/{repo}</c> reference.</param>
    /// <param name="path">The file's repository path.</param>
    /// <param name="content">The file's bytes; tests give each file its own so a response names the file served.</param>
    public void Add(string repository, string path, string content)
    {
        if (!_files.TryGetValue(repository, out var files))
            _files[repository] = files = new Dictionary<string, ProductIconImage>(StringComparer.Ordinal);
        files[path] = new ProductIconImage(System.Text.Encoding.UTF8.GetBytes(content), ProductIconPaths.ContentType(path), path);
    }

    /// <summary>Removes every repository's files and recorded listings.</summary>
    public void Clear()
    {
        _files.Clear();
        Listings.Clear();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAsync(string repository, string branch, CancellationToken ct)
    {
        Listings.Add((repository, branch));
        return Task.FromResult<IReadOnlyList<string>>(
            _files.TryGetValue(repository, out var files) ? [.. files.Keys.Where(ProductIconPaths.IsCandidate)] : []);
    }

    /// <inheritdoc />
    public Task<ProductIconImage?> ReadAsync(string repository, string branch, string path, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(repository, out var files) && files.TryGetValue(path, out var icon) ? icon : null);
}
