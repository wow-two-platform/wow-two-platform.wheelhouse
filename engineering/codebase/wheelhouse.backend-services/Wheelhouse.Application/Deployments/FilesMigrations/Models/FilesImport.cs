namespace Wheelhouse.Application.Deployments.FilesMigrations.Models;

/// <summary>Represents what Files answers to an import or a token request: the import (when one opened) and its
/// token, which only the runner's standard input ever carries.</summary>
/// <param name="Id">The import's id; null for a token of an import that already exists.</param>
/// <param name="Token">The import-scoped token.</param>
/// <param name="ExpiresAt">When the token lapses.</param>
public sealed record FilesImport(string? Id, string Token, DateTimeOffset? ExpiresAt)
{
    /// <inheritdoc />
    public override string ToString() => nameof(FilesImport) + " { " + Id + " }";
}
