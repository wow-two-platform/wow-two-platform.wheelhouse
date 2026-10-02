using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Vaults.Entities;
using Wheelhouse.Infrastructure.Inventory.Mappers;
using Wheelhouse.Infrastructure.Inventory.Models;
using WoW.Two.Sdk.Backend.Beta.Foundation.Serialization;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Infrastructure.Inventory;

/// <summary>Provides the local server's fixtures to the database on the local rig: its products where the database
/// lacks them, and its server, targets and vault as the runner defines them for this run.</summary>
/// <remarks>The rig's hosts, ports and settings paths depend on where Wheelhouse runs, so its server, targets and vault
/// are written again on every start; the operator's own products are never touched.</remarks>
public sealed partial class LocalRigSeedService(
    IDeploymentGateway gateway,
    IProductsRepository products,
    IServersRepository servers,
    ITargetsRepository targets,
    IVaultsRepository vaults,
    ILogger<LocalRigSeedService> logger)
{
    /// <summary>Writes the local server's fixtures into the database.</summary>
    /// <param name="ct">A cancellation token.</param>
    public async Task SeedAsync(CancellationToken ct)
    {
        var read = await gateway.ReadAsync("fixtures", null, ct);
        if (read is not AppResult<JsonElement>.Success { Data: var document })
        {
            LogUnread(logger, ((AppResult<JsonElement>.Failure)read).Error.Message);
            return;
        }

        var fixtures = document.Deserialize<InventorySnapshotModel>(StoredJsonConstants.Default)!;
        foreach (var fixture in fixtures.Products)
        {
            if (await products.GetBySlugAsync(fixture.Slug, ct) is null)
                await products.CreateAsync(new ProductEntity
                {
                    Id = Guid.CreateVersion7(),
                    Slug = fixture.Slug,
                    Name = fixture.Name,
                    Description = fixture.Description,
                    Repository = fixture.Repository,
                    DefaultBranch = fixture.DefaultBranch,
                    Release = InventorySnapshotMapper.Map(fixture.Release),
                }, ct);
        }

        foreach (var fixture in fixtures.Servers)
            await SeedServerAsync(fixture, ct);
        foreach (var fixture in fixtures.Targets)
            await SeedTargetAsync(fixture, ct);
        foreach (var fixture in fixtures.Vaults)
            await SeedVaultAsync(fixture, ct);
    }

    private async Task SeedServerAsync(SnapshotServerModel fixture, CancellationToken ct)
    {
        var server = await servers.GetBySlugAsync(fixture.Id, ct);
        if (server is null)
        {
            await servers.CreateAsync(new ServerEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = fixture.Id,
                Name = fixture.Name,
                Provider = fixture.Provider,
                Host = fixture.Host,
                Region = fixture.Region,
                SshUser = fixture.SshUser,
                SshPort = fixture.SshPort,
                Ingress = InventorySnapshotMapper.Map(fixture.Ingress),
            }, ct);
            return;
        }

        server.Name = fixture.Name;
        server.Provider = fixture.Provider;
        server.Host = fixture.Host;
        server.Region = fixture.Region;
        server.SshUser = fixture.SshUser;
        server.SshPort = fixture.SshPort;
        server.Ingress = InventorySnapshotMapper.Map(fixture.Ingress);
        await servers.UpdateAsync(server, ct);
    }

    private async Task SeedTargetAsync(SnapshotTargetModel fixture, CancellationToken ct)
    {
        var product = await products.GetBySlugAsync(fixture.Product, ct);
        var server = await servers.GetBySlugAsync(fixture.ServerId, ct);
        if (product is null || server is null)
            return;

        var target = await targets.GetBySlugAsync(fixture.Id, ct);
        if (target is null)
        {
            await targets.CreateAsync(new TargetEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = fixture.Id,
                ProductId = product.Id,
                ServerId = server.Id,
                Environment = fixture.Environment,
                Network = fixture.Network,
                Root = fixture.Root,
                Settings = InventorySnapshotMapper.Map(fixture.Settings),
                SmokeChecks = InventorySnapshotMapper.Map(fixture.Smoke),
                Sites = InventorySnapshotMapper.Map(fixture.Sites),
            }, ct);
            return;
        }

        target.ProductId = product.Id;
        target.ServerId = server.Id;
        target.Environment = fixture.Environment;
        target.Network = fixture.Network;
        target.Root = fixture.Root;
        target.Settings = InventorySnapshotMapper.Map(fixture.Settings);
        target.SmokeChecks = InventorySnapshotMapper.Map(fixture.Smoke);
        target.Sites = InventorySnapshotMapper.Map(fixture.Sites);
        await targets.UpdateAsync(target, ct);
    }

    private async Task SeedVaultAsync(SnapshotVaultModel fixture, CancellationToken ct)
    {
        var server = await servers.GetBySlugAsync(fixture.ServerId, ct);
        if (server is null)
            return;

        var vault = await vaults.GetBySlugAsync(fixture.Id, ct);
        if (vault is null)
        {
            await vaults.CreateAsync(new VaultEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = fixture.Id,
                Name = fixture.Name,
                ServerId = server.Id,
                Url = fixture.Url,
            }, ct);
            return;
        }

        vault.Name = fixture.Name;
        vault.ServerId = server.Id;
        vault.Url = fixture.Url;
        await vaults.UpdateAsync(vault, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The local server's fixtures could not be read from the runner ({Reason}).")]
    private static partial void LogUnread(ILogger logger, string reason);
}
