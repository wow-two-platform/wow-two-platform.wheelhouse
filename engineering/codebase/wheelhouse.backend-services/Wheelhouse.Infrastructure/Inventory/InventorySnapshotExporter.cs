using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Inventory.Mappers;
using Wheelhouse.Infrastructure.Inventory.Models;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Storage.FileSystem;
using WoW.Two.Sdk.Beta.Be.Storage.FileSystem.Extensions;

namespace Wheelhouse.Infrastructure.Inventory;

/// <summary>Provides the runner's inventory snapshot: products, servers, targets and vaults written from the database
/// to <c>{root}/inventory.json</c>, replaced whole so the runner never reads half of one.</summary>
/// <remarks>One export runs at a time and reads the database inside its turn, so an older read never overwrites a
/// newer snapshot. The SDK atomic write replaces the file in one rename, owner-only (0600). A failed write is logged and
/// left for the next change or start to repair; <see cref="InventoryExportMonitor"/> lists it in the Issues panel until
/// then.</remarks>
public sealed partial class InventorySnapshotExporter(
    IProductsRepository products,
    IServersRepository servers,
    ITargetsRepository targets,
    IVaultsRepository vaults,
    DeploymentSettings settings,
    InventoryExportMonitor monitor,
    ILogger<InventorySnapshotExporter> logger) : IInventorySnapshot
{
    /// <summary>Holds the snapshot's file name under the runner root; the runner's <c>inventory.py</c> reads the same.</summary>
    public const string FileName = "inventory.json";

    /// <summary>Holds the turn every export in this process waits for.</summary>
    private static readonly SemaphoreSlim Turn = new(1, 1);

    /// <summary>Holds the snapshot's write: owner-only, since it names every server and credential path.</summary>
    private static readonly AtomicFileWriteOptions WriteOptions = new() { UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite };

    /// <inheritdoc />
    public async Task ExportAsync(CancellationToken ct)
    {
        if (settings.Root.Length == 0)
            return;
        try
        {
            await Turn.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            monitor.Failed(InventoryExportMonitor.TimedOut);
            throw;
        }

        try
        {
            var snapshot = await ReadAsync(ct);
            await AtomicFileExtensions.WriteJsonAsync(
                Path.Combine(settings.Root, FileName), snapshot, StoredJsonConstants.Default, WriteOptions, ct);
            monitor.Succeeded();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            monitor.Failed(InventoryExportMonitor.Unwritten);
            LogUnwritten(logger, settings.Root, exception.GetType().Name);
        }
        catch (Exception exception)
        {
            // The after-commit hook or the startup export logs it; the Issues panel names it until an export succeeds.
            monitor.Failed(exception is OperationCanceledException ? InventoryExportMonitor.TimedOut : InventoryExportMonitor.Unread);
            throw;
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
