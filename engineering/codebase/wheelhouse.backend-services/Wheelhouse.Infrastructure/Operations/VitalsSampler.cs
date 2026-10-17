using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Operations;

namespace Wheelhouse.Infrastructure.Operations;

/// <summary>Samples every server's and target's vitals on an interval so the dashboard can show trends. The interval comes from
/// <c>Operations:VitalsSampleMinutes</c> (default 5); zero or less turns sampling off.</summary>
public sealed class VitalsSampler(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<VitalsSampler> logger)
    : BackgroundService
{
    /// <summary>The configuration key holding the sampling interval in minutes.</summary>
    public const string IntervalKey = "Operations:VitalsSampleMinutes";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read when the host starts, so test and local overrides applied after registration still count.
        var minutes = configuration.GetValue(IntervalKey, 5);
        if (minutes <= 0)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var stored = await scope.ServiceProvider.GetRequiredService<VitalsSampling>().SampleOnceAsync(stoppingToken);
                if (stored is null)
                    logger.LogWarning("Vitals sampling skipped: the runner could not read the fleet");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Vitals sampling failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
