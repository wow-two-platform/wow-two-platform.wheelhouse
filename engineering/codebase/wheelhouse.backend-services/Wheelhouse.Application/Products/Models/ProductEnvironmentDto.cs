namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents one environment of a catalog product.</summary>
public sealed record ProductEnvironmentDto
{
    /// <summary>Gets the environment: <c>dev</c> or <c>prod</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the sites the environment publishes, empty before its first rollout.</summary>
    public required IReadOnlyList<ProductSiteDto> Sites { get; init; }

    /// <summary>Gets where the environment's settings belong in a vault, or <c>null</c> when no vault serves it.</summary>
    public ProductSecretsDto? Secrets { get; init; }
}
