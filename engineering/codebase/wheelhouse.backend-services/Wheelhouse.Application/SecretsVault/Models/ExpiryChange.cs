namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents an expiry the owner sets on a secret.</summary>
/// <param name="At">When the secret lapses; null clears the expiry.</param>
public sealed record ExpiryChange(DateTimeOffset? At);
