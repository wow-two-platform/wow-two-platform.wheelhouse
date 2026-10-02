using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.Inventory;

/// <summary>Provides the runner's inventory snapshot at startup, after migrations ran: on the local rig it first seeds
/// the local server's fixtures, then it exports what the database holds.</summary>
public sealed partial class InventoryStartupService(
    IServiceScopeFactory scopes, DeploymentSettings settings, ILogger<InventoryStartupService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            if (settings.IsLocalRig)
                await scope.ServiceProvider.GetRequiredService<LocalRigSeedService>().SeedAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IInventorySnapshot>().ExportAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A start must not fail on the snapshot: the runner keeps the previous one until the next change.
            LogFailed(logger, exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "The inventory snapshot could not be prepared at startup.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
