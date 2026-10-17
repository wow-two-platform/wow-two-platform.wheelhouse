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
using Wheelhouse.Domain.Workspaces.Entities;
using Wheelhouse.Tests.Integration.Harness;

namespace Wheelhouse.Tests.Integration.Support;

/// <summary>Builds inventory rows for the integration tier, each valid as the API would store it.</summary>
public static class InventoryRows
{
    /// <summary>The identifier of the workspace <see cref="Product"/> rows belong to; <see cref="SeedWorkspaceAsync"/>
    /// stores it.</summary>
    public static readonly Guid WorkspaceId = Guid.Parse("0199e5a4-0000-7000-8000-00000000000a");

    /// <summary>A workspace at a place in the order.</summary>
    public static WorkspaceEntity Workspace(string slug, int order, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Slug = slug,
        Name = slug,
        Order = order,
    };

    /// <summary>Stores the workspace every <see cref="Product"/> row belongs to; call it after each reset.</summary>
    public static async Task SeedWorkspaceAsync(WheelhouseTestDb db)
    {
        await using var ctx = db.NewContext();
        ctx.Workspaces.Add(Workspace("own-ventures", 0, WorkspaceId));
        await ctx.SaveChangesAsync();
    }

    /// <summary>A product with a release source of two services, in the seeded workspace.</summary>
    public static ProductEntity Product(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        Name = slug,
        Description = "A test product.",
        Repository = "owner/" + slug,
        DefaultBranch = "main",
        WorkspaceId = WorkspaceId,
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
