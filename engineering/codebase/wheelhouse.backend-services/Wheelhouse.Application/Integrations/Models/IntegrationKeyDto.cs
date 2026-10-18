namespace Wheelhouse.Application.Integrations.Models;

/// <summary>Represents an integration key as the owner sees it — never its secret.</summary>
public sealed record IntegrationKeyDto
{
    /// <summary>Gets the key's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the name the owner gave the key.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the secret's first characters.</summary>
    public required string Prefix { get; init; }

    /// <summary>Gets what the key may reach.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>Gets the owner who created the key.</summary>
    public required string CreatedBy { get; init; }

    /// <summary>Gets when the key was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the key last authenticated a request, or <c>null</c> before its first use.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>Gets when the key was revoked, or <c>null</c> while it is live.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
