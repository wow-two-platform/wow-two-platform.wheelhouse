using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.InfraJobs;

namespace Wheelhouse.Infrastructure.Infra;

/// <summary>Records finished enrollments and placements every <c>Infra:FollowSeconds</c> (default 10; zero or less
/// turns it off): each lands in the audit trail as its owner, and an enrolled server moves to its tailnet address. The
/// jobs themselves run detached and never depend on this pass.</summary>
public sealed class InfraJobFollower(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, ILogger<InfraJobFollower> logger)
    : BackgroundService
{
    /// <summary>The configuration key holding the delay between passes, in seconds.</summary>
    public const string IntervalKey = "Infra:FollowSeconds";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<InfraJobRecorder>().PassAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A process or adapter exception may carry inputs; log only its category.
                    logger.LogWarning("Infrastructure job pass failed ({FailureType})", exception.GetType().Name);
                }

                await Task.Delay(delay, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown stops recording only; detached jobs keep running and are recorded on the next start.
        }
    }
}
