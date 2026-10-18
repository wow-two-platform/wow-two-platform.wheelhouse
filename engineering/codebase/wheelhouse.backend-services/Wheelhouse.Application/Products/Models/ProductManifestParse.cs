namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents the verdict on one product manifest file: the manifest it holds, or the display-safe reason
/// it is ignored.</summary>
public sealed record ProductManifestParse
{
    /// <summary>Gets the manifest, or <c>null</c> when the file is invalid.</summary>
    public ProductManifest? Manifest { get; private init; }

    /// <summary>Gets why the file is ignored, as a sentence safe to show the owner, or <c>null</c> when it is valid.</summary>
    public string? Problem { get; private init; }

    /// <summary>Creates the verdict for a valid file.</summary>
    /// <param name="manifest">What the file holds.</param>
    public static ProductManifestParse Valid(ProductManifest manifest) => new() { Manifest = manifest };

    /// <summary>Creates the verdict for an invalid file.</summary>
    /// <param name="problem">The reason, as one sentence that never quotes the file beyond a key name.</param>
    public static ProductManifestParse Invalid(string problem) => new() { Problem = problem };
}
