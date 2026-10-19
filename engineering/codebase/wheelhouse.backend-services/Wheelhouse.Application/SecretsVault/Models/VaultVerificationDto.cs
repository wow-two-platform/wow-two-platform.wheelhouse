namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents a full decryption check of the built-in vault — after a restore, an import or on demand. Counts
/// and key names only.</summary>
/// <param name="State"><c>unsealed</c> or <c>sealed</c>; a sealed vault checks nothing.</param>
/// <param name="Canary">Whether the canary decrypted to its digest.</param>
/// <param name="Products">The product keys that unwrapped.</param>
/// <param name="Secrets">The secrets checked.</param>
/// <param name="Versions">The versions that decrypted.</param>
/// <param name="Failed">The versions or keys that did not.</param>
/// <param name="Failures">What failed, as <c>product/environment/key v3</c>, at most 50.</param>
public sealed record VaultVerificationDto(
    string State, bool Canary, int Products, int Secrets, int Versions, int Failed, IReadOnlyList<string> Failures);
