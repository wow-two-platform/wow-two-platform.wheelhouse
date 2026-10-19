using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Deployments.Settings.Services;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Runs the scheduled settings applies that are due, every <c>Deployment:ApplySeconds</c> (default 60; zero or
/// less turns it off).</summary>
public sealed class SettingsApplyWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SettingsApplyWorker> logger) : BackgroundService
{
    /// <summary>The configuration key holding the delay between passes, in seconds.</summary>
    public const string IntervalKey = "Deployment:ApplySeconds";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue(IntervalKey, 60);
        if (seconds <= 0)
            return;
        var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 600));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // A fresh scope per pass: each apply acts for the owner who scheduled it.
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SettingsApplyService>().RunDueAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Exceptions from a process or external adapter may contain inputs. Log only the category.
                    logger.LogWarning("Settings apply pass failed ({FailureType})", exception.GetType().Name);
                }

                await Task.Delay(delay, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A due apply waits for the next start.
        }
    }
}
