namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one service a product's <c>deploy.yml</c> declares.</summary>
/// <param name="Service">The service's key under <c>services:</c>.</param>
/// <param name="Image">The image repository it names, or <c>null</c> for the publish workflow's convention.</param>
/// <param name="Needs">The platform services it consumes (<c>postgres</c>, <c>valkey</c>, <c>broker</c>).</param>
public sealed record DeployDescriptorService(string Service, string? Image, IReadOnlyList<string> Needs);
