namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to change a vault's definition.</summary>
public sealed record UpdateVaultApiRequest
{
    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the slug of the server the vault runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets the private management endpoint Wheelhouse reaches.</summary>
    public required string Url { get; init; }
}
