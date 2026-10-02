namespace Wheelhouse.Application.Vaults.Models;

/// <summary>Represents a secrets vault's definition, as the operator edits it; never its credential.</summary>
public sealed record VaultDto
{
    /// <summary>Gets the name routes and the credential folder know the vault by.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the slug of the server the vault runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets the private management endpoint Wheelhouse reaches.</summary>
    public required string Url { get; init; }
}
