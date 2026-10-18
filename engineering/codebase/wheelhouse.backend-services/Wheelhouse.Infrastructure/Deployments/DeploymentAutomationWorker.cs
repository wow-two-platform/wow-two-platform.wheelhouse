using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Deployments.Automation.Services;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Runs the automation passes on an interval: the watch windows, the release watcher, auto-promote and the
/// automatic deploy queue, and the daily retention. The interval comes from <c>Deployment:AutomationSeconds</c>
/// (default 120); zero or less turns automation off.</summary>
public sealed class DeploymentAutomationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<DeploymentAutomationWorker> logger) : BackgroundService
{
    /// <summary>The configuration key holding the delay between passes, in seconds.</summary>
    public const string IntervalKey = "Deployment:AutomationSeconds";

    /// <summary>The configuration key holding how many minutes a successful rollout is watched (default 10; 0 turns the
    /// looks off).</summary>
    public const string WatchKey = "Deployment:WatchMinutes";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read when the host starts, so test and local overrides applied after registration still count.
        var seconds = configuration.GetValue(IntervalKey, 120);
        if (seconds <= 0)
            return;
        var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 3600));
        var watchMinutes = Math.Clamp(configuration.GetValue(WatchKey, 10), 0, 120);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // A fresh scope per pass: no HTTP request, so every audited command records the automation actor.
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DeploymentAutomationService>()
                        .RunOnceAsync(stoppingToken, watchMinutes);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Exceptions from a process or external adapter may contain inputs. Log only the category.
                    logger.LogWarning("Deployment automation pass failed ({FailureType})", exception.GetType().Name);
                }

                await Task.Delay(delay, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown stops automation only; a submitted rollout continues on its target.
        }
    }
}
