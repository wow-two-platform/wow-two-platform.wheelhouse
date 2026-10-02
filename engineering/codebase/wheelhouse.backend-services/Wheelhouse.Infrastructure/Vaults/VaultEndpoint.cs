namespace Wheelhouse.Infrastructure.Vaults;

/// <summary>Represents one vault from the inventory: where Wheelhouse reaches it and which server it runs on.</summary>
/// <param name="Id">The vault's slug.</param>
/// <param name="Name">The display name.</param>
/// <param name="ServerId">The slug of the server the vault runs on.</param>
/// <param name="Url">The management endpoint, without a trailing slash.</param>
public sealed record VaultEndpoint(string Id, string Name, string ServerId, string Url);
