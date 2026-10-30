using Wheelhouse.Application.Catalog;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace Wheelhouse.Application.Products.Services;

/// <summary>Reads a product's <c>engineering/deployment/deploy.yml</c> at its branch head: the services, their needs and
/// the sites with their exposure, and the untrusted PostgreSQL extensions. A product built from several repositories
/// (decision 70) reads each repository that deploys something and merges them as its release sets compose: every service
/// and site once — two repositories naming one is a problem — the primary repository's front door as the product's, and
/// every repository's extensions once each. The file source keeps a head for a few minutes and each commit's file for good, so a read costs GitHub
/// little. A descriptor that cannot be read or parsed comes back with the reason and nothing else.</summary>
public sealed class ProductDescriptorService(IGitHubRepositoryReader files)
{
    /// <summary>Reads the product's descriptor, or its repositories' merged.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>What the descriptor declares, or why it could not be read.</returns>
    public async Task<ProductDescriptorDto> ReadAsync(ProductEntity product, CancellationToken ct)
    {
        var repositories = ProductRepositories.Of(product)
            .Where((item, index) => index == 0 || item.Release is { Images.Count: > 0 })
            .ToList();
        var reads = new List<(string Repository, string Commit, DeployDescriptorModel Descriptor)>();
        foreach (var (repository, _) in repositories)
        {
            var (commit, descriptor, problem) = await ReadOneAsync(repository, product.DefaultBranch, ct);
            if (problem is not null)
                return Problem(problem);
            reads.Add((repository, commit!, descriptor!));
        }

        var services = new List<ProductDescriptorServiceDto>();
        var sites = new List<ProductDescriptorSiteDto>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var siteOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var extensions = new List<string>();
        foreach (var (repository, _, descriptor) in reads)
        {
            // Extensions are the product's databases' own, so repositories may share one.
            foreach (var extension in descriptor.Extensions)
                if (!extensions.Contains(extension, StringComparer.Ordinal))
                    extensions.Add(extension);

            foreach (var service in descriptor.Services)
            {
                if (owners.TryGetValue(service.Service, out var owner))
                    return Problem($"Service '{service.Service}' is in both {owner} and {repository}; a service name belongs to one repository of the product.");
                owners[service.Service] = repository;
                services.Add(new ProductDescriptorServiceDto { Service = service.Service, Needs = service.Needs });
            }

            foreach (var site in descriptor.Sites)
            {
                if (siteOwners.TryGetValue(site.Name, out var owner))
                    return Problem($"Site '{site.Name}' is in both {owner} and {repository}; a site names one host role of the product, so each repository names its own.");
                siteOwners[site.Name] = repository;
                // The product's front door is the primary repository's, else the first repository's that has a site.
                sites.Add(new ProductDescriptorSiteDto
                {
                    Name = site.Name, Exposure = site.Exposure, Primary = site.Primary && !sites.Any(existing => existing.Primary),
                });
            }
        }

        return new ProductDescriptorDto { Commit = reads[0].Commit, Services = services, Sites = sites, Extensions = extensions };
    }

    /// <summary>Reads one repository's descriptor at its branch head.</summary>
    private async Task<(string? Commit, DeployDescriptorModel? Descriptor, string? Problem)> ReadOneAsync(
        string repository, string branch, CancellationToken ct)
    {
        var where = $"{repository}@{branch}";
        var commit = await files.HeadAsync(repository, branch, fresh: false, ct);
        if (commit is null)
            return (null, null, $"{where} could not be read from GitHub.");

        var file = await files.ReadAsync(repository, commit, CatalogConstants.DescriptorPath, CatalogConstants.MaxDescriptorBytes, ct);
        switch (file.Outcome)
        {
            case FileRead.NotFound:
                return (null, null, $"{where} has no {CatalogConstants.DescriptorPath}.");
            case FileRead.TooLarge:
                return (null, null, $"{CatalogConstants.DescriptorPath} in {where} is too large to read.");
            case not FileRead.Found:
                return (null, null, $"{CatalogConstants.DescriptorPath} in {where} could not be read.");
        }

        var parsed = DeployDescriptorParser.Parse(file.Content!);
        return parsed.Value is { } descriptor
            ? (commit, descriptor, null)
            : (null, null, $"deploy.yml in {where} {parsed.Problem}");
    }

    private static ProductDescriptorDto Problem(string problem) => new() { Problem = problem };
}
