namespace Wheelhouse.Application.Abstractions;

/// <summary>Refers to what reading a product's manifest file at one commit gave.</summary>
public enum ProductManifestFileStatus
{
    /// <summary>The commit carries the file; its bytes came back.</summary>
    Found,

    /// <summary>The commit does not carry the file — a verdict that never changes for that commit.</summary>
    Missing,

    /// <summary>The file is larger than the manifest limit and was not read — a verdict that never changes either.</summary>
    TooLarge,

    /// <summary>The repository gave no verdict, so nothing is known about the file.</summary>
    Unreadable,
}
