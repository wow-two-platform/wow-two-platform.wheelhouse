namespace Wheelhouse.Api.Requests;

/// <summary>Sets a secret's expiry.</summary>
/// <param name="ExpiresAt">When it lapses; null clears the expiry.</param>
public sealed record SecretExpiryRequest(DateTimeOffset? ExpiresAt = null);
