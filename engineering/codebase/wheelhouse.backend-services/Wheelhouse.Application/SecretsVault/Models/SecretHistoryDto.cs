namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents a secret's history in one environment, newest version first.</summary>
/// <param name="Target">The target whose environment holds it.</param>
/// <param name="Key">The configuration path.</param>
/// <param name="Removed">Whether the owner removed it; its versions stay for the audit.</param>
/// <param name="ExpiresAt">When it lapses, if the owner set an expiry.</param>
/// <param name="Versions">Every version.</param>
public sealed record SecretHistoryDto(
    string Target, string Key, bool Removed, DateTimeOffset? ExpiresAt, IReadOnlyList<SecretVersionDto> Versions);
