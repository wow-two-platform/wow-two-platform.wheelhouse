using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Application.Workspaces.Services;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Vaults.Entities;
using Wheelhouse.Infrastructure.Inventory.Mappers;
using Wheelhouse.Infrastructure.Inventory.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Inventory;

/// <summary>Provides the local server's fixtures to the database on the local rig: its products where the database
/// lacks them, in the default workspace, and its server, targets and vault as the runner defines them for this run.</summary>
/// <remarks>The rig's hosts, ports and settings paths depend on where Wheelhouse runs, so its server, targets and vault
/// are written again on every start; the owner's own products are never touched.</remarks>
public sealed partial class LocalRigSeedService(
    IDeploymentGateway gateway,
    IProductsRepository products,
    IServersRepository servers,
    ITargetsRepository targets,
    IVaultsRepository vaults,
    DefaultWorkspaceService defaultWorkspace,
    ILogger<LocalRigSeedService> logger,
    SecretsVaultService? vault = null,
    ISecretsVaultStore? vaultStore = null,
    SecretsVaultSettings? vaultSettings = null,
    IMasterKeyBroker? vaultKey = null)
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
                    WorkspaceId = (await defaultWorkspace.GetAsync(ct)).Id,
                    Release = InventorySnapshotMapper.Map(fixture.Release),
                    Components = InventorySnapshotMapper.Map(fixture.Components),
                }, ct);
        }

        foreach (var fixture in fixtures.Servers)
            await SeedServerAsync(fixture, ct);
        foreach (var fixture in fixtures.Targets)
            await SeedTargetAsync(fixture, ct);
        foreach (var fixture in fixtures.Vaults)
            await SeedVaultAsync(fixture, ct);
        await SeedSecretsAsync(document, ct);
    }

    /// <summary>Seeds the rig's test secrets into the built-in vault, each once: a secret the vault already holds keeps
    /// its value. Skipped while the vault is sealed or the standalone fallback is on.</summary>
    private async Task SeedSecretsAsync(JsonElement document, CancellationToken ct)
    {
        if (vault is null || vaultStore is null || vaultSettings?.IsExternal == true
            || !document.TryGetProperty("secrets", out var items) || items.ValueKind != JsonValueKind.Array)
            return;
        WriteRigKey();
        var seeded = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "product") is not { } slug || Text(item, "key") is not { } key || Text(item, "value") is not { } value
                || !Enum.TryParse<Wheelhouse.Domain.Targets.Enums.DeploymentEnvironment>(Text(item, "environment"), true, out var environment)
                || await products.GetBySlugAsync(slug, ct) is not { } product
                || await vaultStore.GetSecretAsync(product.Id, environment, key, ct) is not null)
                continue;
            var written = await vault.StageWritesAsync(product.Id, environment, [new SecretWrite(key, value, "rig test value")],
                "local-rig", "rig fixture", ct);
            if (written is AppResult<IReadOnlyList<SecretWritten>>.Failure)
                return; // sealed: the rig wrote no root key yet
            seeded++;
        }
        if (seeded > 0)
        {
            await vaultStore.SaveAsync(ct);
            LogSeededSecrets(logger, seeded);
        }
    }

    /// <summary>Gives the rig's built-in vault a root key of its own when it has none: random, mode 0600, beside the
    /// rig's other credentials. Only the rig reaches here; a deployed control plane's key is the owner's to place.</summary>
    private void WriteRigKey()
    {
        if (vaultKey is null || vaultKey.Read().Problem != MasterKeyRead.Missing || vaultKey.Location.Length == 0)
            return;
        var folder = Path.GetDirectoryName(vaultKey.Location)!;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(folder);
        else
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(vaultKey.Location, options);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(Convert.ToBase64String(key));
            LogRigKey(logger);
        }
        catch (IOException)
        {
            // Another start wrote it first.
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} rig test secrets into the built-in vault")]
    private static partial void LogSeededSecrets(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote the rig's own root key for the built-in vault")]
    private static partial void LogRigKey(ILogger logger);
}
