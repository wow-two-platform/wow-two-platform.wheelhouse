namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents the outcome of reading one repository file at one commit.</summary>
/// <param name="Status">What the read gave.</param>
/// <param name="Content">The file's bytes when <paramref name="Status"/> is <see cref="RepositoryFileStatus.Found"/>.</param>
public sealed record RepositoryFileRead(RepositoryFileStatus Status, byte[]? Content = null);
