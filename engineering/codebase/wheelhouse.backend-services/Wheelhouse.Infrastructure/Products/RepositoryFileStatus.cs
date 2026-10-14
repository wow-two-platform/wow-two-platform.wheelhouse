namespace Wheelhouse.Infrastructure.Products;

/// <summary>Refers to what reading one repository file at one commit gave.</summary>
public enum RepositoryFileStatus
{
    /// <summary>The commit carries the file and it fits the limit; its bytes came back.</summary>
    Found,

    /// <summary>GitHub answered that the commit has no such file — final for that commit.</summary>
    Missing,

    /// <summary>The file is larger than the limit and was not read in full — final for that commit.</summary>
    TooLarge,

    /// <summary>GitHub gave no verdict (unreachable, throttled, refused), so nothing is known about the file.</summary>
    Unreadable,
}
