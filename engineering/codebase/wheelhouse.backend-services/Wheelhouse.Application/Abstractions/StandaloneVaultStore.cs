namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents a standalone vault's stored contents.</summary>
/// <param name="Namespaces">Each namespace by slug.</param>
public sealed record StandaloneVaultStore(IReadOnlyList<StandaloneNamespace> Namespaces);
