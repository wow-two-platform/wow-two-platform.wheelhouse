namespace Wheelhouse.Application.Abstractions;

/// <summary>Refers to what reading one repository file at one commit gave.</summary>
public enum RepositoryFileStatus
{
    /// <summary>The commit carries the file; its bytes came back.</summary>
    Found,

    /// <summary>The commit does not carry the file.</summary>
    Missing,

    /// <summary>The file is larger than the caller's limit and was not read.</summary>
    TooLarge,

    /// <summary>GitHub gave no verdict: unreachable, or the token cannot see the repository.</summary>
    Unreadable,
}
