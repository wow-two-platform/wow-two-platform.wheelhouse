using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Deployments.Fleets.Services;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Moves the running fleet rollout on every <c>Deployment:FleetSeconds</c> (default 10; zero or less turns it
/// off): records the finished outcome of the product it is on and submits the next. A pass with no running fleet reads
/// one row and nothing else.</summary>
public sealed class FleetRolloutWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<FleetRolloutWorker> logger) : BackgroundService
{
    /// <summary>The configuration key holding the delay between passes, in seconds.</summary>
    public const string IntervalKey = "Deployment:FleetSeconds";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read when the host starts, so test and local overrides applied after registration still count.
        var seconds = configuration.GetValue(IntervalKey, 10);
        if (seconds <= 0)
            return;
        var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 300));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // A fresh scope per pass: no HTTP request, so the fleet's deploys act for the owner who started it.
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<FleetRolloutService>().AdvanceRunningAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Exceptions from a process or external adapter may contain inputs. Log only the category.
                    logger.LogWarning("Fleet rollout pass failed ({FailureType})", exception.GetType().Name);
                }

                await Task.Delay(delay, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown pauses the fleet; the next start continues it, and a submitted rollout continues on its target.
        }
    }
}
