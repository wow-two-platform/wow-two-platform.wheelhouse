namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents one namespace of a standalone vault, such as <c>foreverpin-prod</c>.</summary>
/// <param name="Slug">The namespace's slug, the AAD of its data keys.</param>
/// <param name="DataKeys">Its data keys by identifier, wrapped by the standalone vault's master key.</param>
/// <param name="Secrets">Its secrets.</param>
public sealed record StandaloneNamespace(
    string Slug, IReadOnlyDictionary<Guid, StandaloneSealed> DataKeys, IReadOnlyList<StandaloneSecret> Secrets);
