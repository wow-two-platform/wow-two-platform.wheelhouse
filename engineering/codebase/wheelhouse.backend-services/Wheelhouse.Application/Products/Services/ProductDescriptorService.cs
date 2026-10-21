using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Application.Products.Services;

/// <summary>Reads a product's <c>engineering/deployment/deploy.yml</c> at its branch head: the services, their needs and
/// the sites with their exposure. The file source keeps a head for a few minutes and each commit's file for good, so a
/// read costs GitHub little. A descriptor that cannot be read or parsed comes back with the reason and nothing else.</summary>
public sealed class ProductDescriptorService(IRepositoryFileSource files)
{
    /// <summary>Reads the product's descriptor.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>What the descriptor declares, or why it could not be read.</returns>
    public async Task<ProductDescriptorDto> ReadAsync(ProductEntity product, CancellationToken ct)
    {
        var where = $"{product.Repository}@{product.DefaultBranch}";
        var commit = await files.HeadAsync(product.Repository, product.DefaultBranch, fresh: false, ct);
        if (commit is null)
            return Problem($"{where} could not be read from GitHub.");

        var file = await files.ReadAsync(
            product.Repository, commit, CatalogConstants.DescriptorPath, CatalogConstants.MaxDescriptorBytes, ct);
        switch (file.Status)
        {
            case RepositoryFileStatus.Missing:
                return Problem($"{where} has no {CatalogConstants.DescriptorPath}.");
            case RepositoryFileStatus.TooLarge:
                return Problem($"{CatalogConstants.DescriptorPath} in {where} is too large to read.");
            case RepositoryFileStatus.Unreadable:
                return Problem($"{CatalogConstants.DescriptorPath} in {where} could not be read.");
        }

        var parsed = DeployDescriptorParser.Parse(file.Content!);
        if (parsed.Value is not { } descriptor)
            return Problem($"deploy.yml in {where} {parsed.Problem}");

        return new ProductDescriptorDto
        {
            Commit = commit,
            Services =
            [
                .. descriptor.Services.Select(service => new ProductDescriptorServiceDto
                {
                    Service = service.Service, Needs = service.Needs,
                }),
            ],
            Sites =
            [
                .. descriptor.Sites.Select(site => new ProductDescriptorSiteDto
                {
                    Name = site.Name, Exposure = site.Exposure, Primary = site.Primary,
                }),
            ],
        };
    }

    private static ProductDescriptorDto Problem(string problem) => new() { Problem = problem };
}
