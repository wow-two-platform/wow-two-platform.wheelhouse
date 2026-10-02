using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Inventory.Mappers;
using Wheelhouse.Infrastructure.Inventory.Models;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Backend.Beta.Foundation.Serialization;

namespace Wheelhouse.Infrastructure.Inventory;

/// <summary>Provides the runner's inventory snapshot: products, servers, targets and vaults written from the database
/// to <c>{root}/inventory.json</c>, replaced whole so the runner never reads half of one.</summary>
/// <remarks>One export runs at a time and reads the database inside its turn, so an older read never overwrites a
/// newer snapshot. A failed write is logged and left for the next change or start to repair.</remarks>
public sealed partial class InventorySnapshotExporter(
    IProductsRepository products,
    IServersRepository servers,
    ITargetsRepository targets,
    IVaultsRepository vaults,
    DeploymentSettings settings,
    ILogger<InventorySnapshotExporter> logger) : IInventorySnapshot
{
    /// <summary>Holds the snapshot's file name under the runner root; the runner's <c>inventory.py</c> reads the same.</summary>
    public const string FileName = "inventory.json";

    /// <summary>Holds the turn every export in this process waits for.</summary>
    private static readonly SemaphoreSlim Turn = new(1, 1);

    /// <inheritdoc />
    public async Task ExportAsync(CancellationToken ct)
    {
        if (settings.Root.Length == 0)
            return;
        await Turn.WaitAsync(ct);
        try
        {
            var snapshot = await ReadAsync(ct);
            Directory.CreateDirectory(settings.Root);
            var path = Path.Combine(settings.Root, FileName);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await JsonSerializer.SerializeAsync(stream, snapshot, StoredJsonConstants.Default, ct);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnwritten(logger, settings.Root, exception.GetType().Name);
        }
        finally
        {
            Turn.Release();
        }
    }

    private async Task<InventorySnapshotModel> ReadAsync(CancellationToken ct)
    {
        var productList = await products.GetAllAsync(ct);
        var serverList = await servers.GetAllAsync(ct);
        var productSlugs = productList.ToDictionary(product => product.Id, product => product.Slug);
        var serverSlugs = serverList.ToDictionary(server => server.Id, server => server.Slug);
        return new InventorySnapshotModel
        {
            Products = [.. productList.Select(InventorySnapshotMapper.Map)],
            Servers = [.. serverList.Select(InventorySnapshotMapper.Map)],
            Targets =
            [
                .. (await targets.GetAllAsync(ct)).Select(target =>
                    InventorySnapshotMapper.Map(target, productSlugs[target.ProductId], serverSlugs[target.ServerId])),
            ],
            Vaults = [.. (await vaults.GetAllAsync(ct)).Select(vault => InventorySnapshotMapper.Map(vault, serverSlugs[vault.ServerId]))],
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The runner's inventory snapshot under {Root} could not be written ({Reason}); the runner keeps the previous one.")]
    private static partial void LogUnwritten(ILogger logger, string root, string reason);
}
