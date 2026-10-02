namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to add a vault.</summary>
public sealed record CreateVaultApiRequest
{
    /// <summary>Gets the name routes and the credential folder will know the vault by.</summary>
    public required string Slug { get; init; }    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the slug of the server the vault runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets the private management endpoint Wheelhouse reaches.</summary>
    public required string Url { get; init; }
}
