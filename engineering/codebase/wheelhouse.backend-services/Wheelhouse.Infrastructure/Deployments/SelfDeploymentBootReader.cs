using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.SelfDeploy.Models;
using Wheelhouse.Application.Deployments.SelfDeploy.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Reads this console's own newest rollout from the host's journal once it boots: a self-promotion's runner
/// job outlives the console it replaced, so the new console — or the old one again, after a roll back — learns how it
/// ended, logs it and keeps it for the console to show. While the rollout still runs (the runner probes and tidies after
/// the swap) it reads again, every <c>Deployment:SelfReadSeconds</c> (default 15; zero turns it off), for ten minutes.</summary>
/// <remarks>Taking <see cref="IConsoleIdentity"/> builds it as the host starts, so its start time is the console's.</remarks>
public sealed partial class SelfDeploymentBootReader(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, IConsoleIdentity console,
    ILogger<SelfDeploymentBootReader> logger)
    : BackgroundService
{
    /// <summary>The configuration key holding the delay between reads, in seconds; zero or less turns the reader off.</summary>
    public const string IntervalKey = "Deployment:SelfReadSeconds";

    /// <summary>Holds how long a rollout still under way is read again.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue(IntervalKey, 15);
        if (seconds <= 0)
            return;
        var delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300));
        var until = time.GetUtcNow() + Window;
        try
        {
            // The inventory export and the first observation pass run first.
            await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var latest = await ReadAsync(stoppingToken);
                if (latest is null || latest.Outcome is not ("pending" or "unknown") || time.GetUtcNow() > until)
                    return;
                await Task.Delay(delay, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown stops the read only; the host's runner job continues on its own.
        }
    }

    /// <summary>One read of this console's own newest rollout, logged; null when there is none or it cannot be read.</summary>
    private async Task<SelfDeploymentRunDto?> ReadAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(new SelfDeploymentQuery(), ct);
            if (result is not AppResult<SelfDeploymentDto>.Success { Data: { Latest: { } latest } data })
                return null;
            if (latest.BeforeBoot)
                LogOutcome(logger, data.Target ?? "?", latest.Release ?? "?", latest.Status, console.BootedAt);
            return latest;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Exceptions from a process or external adapter may contain inputs. Log only the category.
            LogFailed(logger, exception.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "This console's own rollout to {Target}: {Release} {Status} (console started {BootedAt}).")]
    private static partial void LogOutcome(ILogger logger, string target, string release, string status, DateTimeOffset bootedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "This console's own rollout could not be read ({FailureType}).")]
    private static partial void LogFailed(ILogger logger, string failureType);
}
