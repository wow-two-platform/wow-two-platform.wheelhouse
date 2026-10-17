using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Products;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace Wheelhouse.Infrastructure.Catalog;

/// <summary>Reads repository files through the shared <see cref="GitHubRepositoryReader"/>, with the signed-in operator's
/// token and the reader's per-commit cache.</summary>
public sealed class GitHubRepositoryFileSource(GitHubRepositoryReader reader) : IRepositoryFileSource
{
    /// <inheritdoc />
    public Task<string?> HeadAsync(string repository, string branch, bool fresh, CancellationToken ct) =>
        reader.HeadAsync(repository, branch, fresh, ct);

    /// <inheritdoc />
    public async Task<RepositoryFileRead> ReadAsync(string repository, string commit, string path, int maxBytes, CancellationToken ct)
    {
        var file = await reader.ReadAsync(repository, commit, path, maxBytes, ct);
        return new RepositoryFileRead(
            file.Outcome switch
            {
                FileRead.Found => RepositoryFileStatus.Found,
                FileRead.NotFound => RepositoryFileStatus.Missing,
                FileRead.TooLarge => RepositoryFileStatus.TooLarge,
                _ => RepositoryFileStatus.Unreadable,
            },
            file.Content);
    }
}
