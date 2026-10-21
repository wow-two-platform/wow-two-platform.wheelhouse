namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents what a product's <c>deploy.yml</c> declares at its branch head: the services every environment
/// runs, what each needs from the host, and the sites the product publishes. A placement offers only what it calls for.</summary>
public sealed record ProductDescriptorDto
{
    /// <summary>Gets the full SHA of the commit the descriptor was read from; <c>null</c> when it could not be read.</summary>
    public string? Commit { get; init; }

    /// <summary>Gets why the descriptor could not be read or parsed; <c>null</c> when it was.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets each service, in file order.</summary>
    public IReadOnlyList<ProductDescriptorServiceDto> Services { get; init; } = [];

    /// <summary>Gets each site the product publishes.</summary>
    public IReadOnlyList<ProductDescriptorSiteDto> Sites { get; init; } = [];

    /// <summary>Gets whether any service needs PostgreSQL, so the product takes a dev and a prod database.</summary>
    public bool NeedsPostgres => Services.Any(service => service.Needs.Contains("postgres", StringComparer.Ordinal));
}
