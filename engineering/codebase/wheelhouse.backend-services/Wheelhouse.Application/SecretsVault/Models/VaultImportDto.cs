namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents what an import from a standalone vault did, in counts; never a value.</summary>
/// <param name="Source">Where it read from: <c>database</c> or the inventory vault's slug.</param>
/// <param name="Namespaces">The namespaces that matched a product environment (<c>{product}-{environment}</c>).</param>
/// <param name="Unmatched">The namespaces left alone, by slug.</param>
/// <param name="Secrets">The secrets imported.</param>
/// <param name="Versions">The versions imported.</param>
/// <param name="Skipped">The secrets the built-in vault already held, left as they were.</param>
/// <param name="Verified">The imported versions that decrypted again to the value read.</param>
/// <param name="Failed">The secrets or versions that could not be read or verified; any failure keeps nothing.</param>
/// <param name="Failures">What failed, as <c>namespace/key</c>, at most 50.</param>
public sealed record VaultImportDto(
    string Source, int Namespaces, IReadOnlyList<string> Unmatched, int Secrets, int Versions, int Skipped, int Verified,
    int Failed, IReadOnlyList<string> Failures);
