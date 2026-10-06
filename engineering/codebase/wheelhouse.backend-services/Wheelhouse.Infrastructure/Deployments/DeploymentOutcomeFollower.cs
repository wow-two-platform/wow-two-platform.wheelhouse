using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Observes durable submissions independently of the browser. The runner reads target journals and
/// persists its observation schedule; this service never starts, retries or reconciles a deployment.</summary>
public sealed class DeploymentOutcomeFollower(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<DeploymentOutcomeFollower> logger) : BackgroundService
{
    /// <summary>The delay between bounded observation passes; zero disables the follower.</summary>
    public const string IntervalKey = "Deployment:FollowSeconds";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read after host configuration overrides have been applied.
        var seconds = configuration.GetValue(IntervalKey, 10);
        if (seconds <= 0)
            return;
        var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<IDeploymentGateway>()
                        .ReadAsync("follow", null, stoppingToken);
                    if (result is AppResult<JsonElement>.Failure)
                        logger.LogWarning("Deployment observations could not be refreshed; the next pass will retry");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Exceptions from a process or external adapter may contain inputs. Log only the category.
                    logger.LogWarning("Deployment observation pass failed ({FailureType})", exception.GetType().Name);
                }

                await Task.Delay(delay, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown cancels observation only. The detached target worker continues independently.
        }
    }
}
