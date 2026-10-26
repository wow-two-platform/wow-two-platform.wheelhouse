namespace Wheelhouse.Api.Requests;

/// <summary>Represents the query of a request for the release source a product's repository gives.</summary>
public sealed record ProductReleaseSourceApiRequest
{
    /// <summary>Gets the product's slug; the release asset is named after it.</summary>
    public string Slug { get; init; } = "";

    /// <summary>Gets the repository, as <c>owner/name</c>.</summary>
    public string Repository { get; init; } = "";

    /// <summary>Gets the branch whose <c>deploy.yml</c> is read.</summary>
    public string Branch { get; init; } = "main";

    /// <summary>Gets whether to ask GitHub for the branch head now, as the form's Refresh does.</summary>
    public bool Refresh { get; init; }
}
