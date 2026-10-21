namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one repository's release inside a release set: a product built from several repositories
/// releases one of each (decision 70).</summary>
public sealed record ReleaseComponentDto
{
    /// <summary>Gets the repository's name within the product (<c>studio</c>).</summary>
    public string? Name { get; init; }

    /// <summary>Gets the repository, as <c>owner/name</c>.</summary>
    public string? Repository { get; init; }

    /// <summary>Gets the repository's release tag, or <c>sha-…</c> for a commit build.</summary>
    public string? Release { get; init; }

    /// <summary>Gets <c>release</c> or <c>candidate</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Gets the commit the repository's release was built from.</summary>
    public string? SourceCommit { get; init; }

    /// <summary>Gets the services the repository contributes.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}
