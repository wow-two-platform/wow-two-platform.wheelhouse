namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents one secret value to write as a new version.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Value">The value; never logged.</param>
/// <param name="Note">The owner's note on the change.</param>
/// <param name="Expiry">The expiry to set: null keeps the secret's, a change with no instant clears it.</param>
public sealed record SecretWrite(string Key, string Value, string? Note = null, ExpiryChange? Expiry = null)
{
    /// <summary>Returns the write without its value.</summary>
    public override string ToString() => nameof(SecretWrite) + " { " + Key + " }";
}
