namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents the verdict on the identity one repository file declares: the manifest it holds, the
/// display-safe reason it is ignored, or that the file declares none.</summary>
public sealed record ProductManifestParse
{
    /// <summary>Gets the verdict for a file that declares no identity, such as a descriptor without identity keys.</summary>
    public static ProductManifestParse Absent { get; } = new();

    /// <summary>Gets the manifest, or <c>null</c> when the file is invalid or declares none.</summary>
    public ProductManifest? Manifest { get; private init; }

    /// <summary>Gets why the file is ignored, as a sentence safe to show the owner, or <c>null</c> when it is valid or
    /// declares none.</summary>
    public string? Problem { get; private init; }

    /// <summary>Gets whether the file declares no identity at all.</summary>
    public bool IsAbsent => Manifest is null && Problem is null;

    /// <summary>Creates the verdict for a valid file.</summary>
    /// <param name="manifest">What the file holds.</param>
    public static ProductManifestParse Valid(ProductManifest manifest) => new() { Manifest = manifest };

    /// <summary>Creates the verdict for an invalid file.</summary>
    /// <param name="problem">The reason, as one sentence that never quotes the file beyond a key name.</param>
    public static ProductManifestParse Invalid(string problem) => new() { Problem = problem };
}
