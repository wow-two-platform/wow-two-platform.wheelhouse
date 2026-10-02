using Wheelhouse.Domain.Integrations.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Domain.Vaults.Entities;

namespace Wheelhouse.Tests.Integration.Support;

/// <summary>Builds inventory rows for the integration tier, each valid as the API would store it.</summary>
public static class InventoryRows
{
    /// <summary>A product with a release source of two services.</summary>
    public static ProductEntity Product(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        Name = slug,
        Description = "A test product.",
        Repository = "owner/" + slug,
        DefaultBranch = "main",
        Release = new ProductReleaseValueObject
        {
            Asset = slug + "-release.tar.gz",
            Workflow = "publish.yml",
            Images =
            [
                new ReleaseImageValueObject { Service = "api", Image = "ghcr.io/owner/" + slug + "/api" },
                new ReleaseImageValueObject { Service = "web", Image = "ghcr.io/owner/" + slug + "/web" },
            ],
        },
    };

    /// <summary>A Hetzner server with a preview host pattern.</summary>
    public static ServerEntity Server(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        Name = slug,
        Provider = VpsProvider.Hetzner,
        Host = slug + ".example.net",
        Region = "hel1",
        SshUser = "deploy",
        SshPort = 22,
        Ingress = new ServerIngressValueObject { Pattern = "{site}-{product}.{environment}.preview.example", PrivateEntryPoints = ["private"] },
    };

    /// <summary>A target with one settings file, one smoke check and one named site.</summary>
    public static TargetEntity Target(string slug, ProductEntity product, ServerEntity server) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        ProductId = product.Id,
        ServerId = server.Id,
        Environment = DeploymentEnvironment.Prod,
        Network = "platform",
        Root = "/srv/wheelhouse",
        Settings = [new TargetSettingValueObject { Service = "api", Path = "/srv/settings/" + slug + "/api.json" }],
        SmokeChecks = [new SmokeCheckValueObject { Service = "api", Path = "/health", Status = 200 }],
        Sites = [new SiteHostValueObject { Site = "app", Host = slug + ".example.com" }],
    };

    /// <summary>A vault on a server.</summary>
    public static VaultEntity Vault(string slug, ServerEntity server) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        Name = slug,
        ServerId = server.Id,
        Url = "http://vault:8080",
    };

    /// <summary>A live integration key granting the catalog scope.</summary>
    public static IntegrationKeyEntity Key(string name, string hash, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Prefix = "wh_abcdefgh",
        Hash = hash,
        Scopes = "catalog:read",
        CreatedBy = "test-admin",
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };
}
