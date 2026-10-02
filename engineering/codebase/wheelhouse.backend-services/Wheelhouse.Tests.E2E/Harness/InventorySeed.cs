using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Domain.Vaults.Entities;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Writes the standard inventory every E2E test starts from, straight through the repositories so it leaves
/// no audit entries: ForeverPin on a dev and a prod server, Wheelhouse with no environment, and a vault beside dev.</summary>
public static class InventorySeed
{
    /// <summary>Writes the standard inventory into the freshly reset database.</summary>
    /// <param name="fixture">The shared host.</param>
    public static async Task SeedAsync(WheelhouseAppFixture fixture)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var products = services.GetRequiredService<IProductsRepository>();
        var servers = services.GetRequiredService<IServersRepository>();

        var foreverPin = await products.CreateAsync(new ProductEntity
        {
            Id = Guid.NewGuid(), Slug = "foreverpin", Name = "ForeverPin", Description = "Styled QR codes and short links.",
            Repository = "sulton-max/10x-venture-forever-pin", DefaultBranch = "main",
            Release = new ProductReleaseValueObject
            {
                Asset = "foreverpin-release.tar.gz", Workflow = "publish-docker-image.yml",
                Images = [new ReleaseImageValueObject { Service = "management", Image = "ghcr.io/sulton-max/10x-venture-forever-pin/management" }],
            },
        });
        await products.CreateAsync(new ProductEntity
        {
            Id = Guid.NewGuid(), Slug = "wheelhouse", Name = "Wheelhouse", Description = "The portfolio's control plane.",
            Repository = "wow-two-platform/wow-two-platform.wheelhouse", DefaultBranch = "main",
        });

        var pilot = await servers.CreateAsync(Server("pilot-host", "Pilot", "vps.example.net", "hel1"));
        var prod = await servers.CreateAsync(Server("prod-host", "Prod", "prod.example.net", "fsn1"));

        var targets = services.GetRequiredService<ITargetsRepository>();
        await targets.CreateAsync(Target("foreverpin-dev", foreverPin, pilot, DeploymentEnvironment.Dev));
        await targets.CreateAsync(Target("foreverpin-prod", foreverPin, prod, DeploymentEnvironment.Prod));
        await services.GetRequiredService<IVaultsRepository>().CreateAsync(new VaultEntity
        {
            Id = Guid.NewGuid(), Slug = "pilot-vault", Name = "Pilot vault", ServerId = pilot.Id, Url = "http://vault:8080",
        });
    }

    private static ServerEntity Server(string slug, string name, string host, string region) => new()
    {
        Id = Guid.NewGuid(), Slug = slug, Name = name, Provider = VpsProvider.Hetzner, Host = host, Region = region,
        SshUser = "deploy", SshPort = 22, Ingress = new ServerIngressValueObject(),
    };

    private static TargetEntity Target(string slug, ProductEntity product, ServerEntity server, DeploymentEnvironment environment) => new()
    {
        Id = Guid.NewGuid(), Slug = slug, ProductId = product.Id, ServerId = server.Id, Environment = environment,
        Network = "platform", Root = "/srv/wheelhouse",
        Settings = [new TargetSettingValueObject { Service = "management", Path = "/srv/settings/" + slug + "/management.json" }],
    };
}
