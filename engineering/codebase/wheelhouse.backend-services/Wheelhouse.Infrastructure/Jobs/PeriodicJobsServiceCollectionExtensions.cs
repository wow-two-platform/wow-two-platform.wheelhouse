using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Automation.Services;
using Wheelhouse.Application.Deployments.Following.Services;
using Wheelhouse.Application.Deployments.Fleets.Services;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Operations;
using WoW.Two.Sdk.Beta.Be.Jobs.Core;

namespace Wheelhouse.Infrastructure.Jobs;

/// <summary>Registers Wheelhouse's background passes as SDK periodic jobs. Each reads its interval when the host starts, so a
/// test host's or local run's override counts; zero or less turns it off. A pass runs in a fresh scope with no HTTP
/// request, so audited commands record the automation or the owner who started the work. A failed pass logs only its
/// exception's type, since a runner or adapter exception may carry inputs, and the next pass runs on schedule.</summary>
public static partial class PeriodicJobsServiceCollectionExtensions
{
    /// <summary>Holds the job sampling every server's and target's vitals.</summary>
    public const string VitalsJob = "vitals-sampling";

    /// <summary>Holds the job observing durable submissions independently of the browser.</summary>
    public const string FollowJob = "deployment-following";

    /// <summary>Holds the job running the automation passes.</summary>
    public const string AutomationJob = "deployment-automation";

    /// <summary>Holds the job moving the running fleet rollout.</summary>
    public const string FleetJob = "fleet-rollout";

    /// <summary>Holds the job running the scheduled settings applies that are due.</summary>
    public const string ApplyJob = "settings-apply";

    /// <summary>Holds the job recording finished infrastructure jobs.</summary>
    public const string InfraFollowJob = "infra-following";

    /// <summary>Holds the sampling interval in minutes (default 5, no bounds).</summary>
    public const string VitalsMinutesKey = "Operations:VitalsSampleMinutes";

    /// <summary>Holds the delay between observation passes in seconds (default 10, bounded 1–300): how often a pass asks
    /// whether the runner is due, which it is while a submission is unfinished.</summary>
    public const string FollowSecondsKey = "Deployment:FollowSeconds";

    /// <summary>Holds how many seconds the deployment follower waits between reads while no submission is unfinished
    /// (default 300, bounded 30–3600): the safety pass that catches a rollout started outside the console.</summary>
    public const string FollowIdleSecondsKey = "Deployment:FollowIdleSeconds";

    /// <summary>Holds the delay between automation passes in seconds (default 120, bounded 10–3600).</summary>
    public const string AutomationSecondsKey = "Deployment:AutomationSeconds";

    /// <summary>Holds how many minutes a successful rollout is watched (default 10, bounded 0–120; 0 turns the looks off).</summary>
    public const string WatchMinutesKey = "Deployment:WatchMinutes";

    /// <summary>Holds the delay between fleet passes in seconds (default 10, bounded 2–300).</summary>
    public const string FleetSecondsKey = "Deployment:FleetSeconds";

    /// <summary>Holds the delay between settings apply passes in seconds (default 60, bounded 10–600).</summary>
    public const string ApplySecondsKey = "Deployment:ApplySeconds";

    /// <summary>Holds the delay between infrastructure job passes in seconds (default 10, bounded 2–300): how often a pass
    /// asks whether the runner is due, which it is while a job is unfinished.</summary>
    public const string InfraFollowSecondsKey = "Infra:FollowSeconds";

    /// <summary>Holds how many seconds the infrastructure follower waits between reads while no job is unfinished
    /// (default 300, bounded 30–3600).</summary>
    public const string InfraFollowIdleSecondsKey = "Infra:FollowIdleSeconds";

    /// <summary>Samples every server's and target's vitals each <see cref="VitalsMinutesKey"/> minutes on a fixed rate, so the
    /// dashboard can show trends.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddVitalsSamplingJob(this IServiceCollection services) =>
        services.AddPeriodicJob(
            VitalsJob,
            async (scope, ct) =>
            {
                if (await scope.GetRequiredService<VitalsSampling>().SampleOnceAsync(ct) is null)
                    LogVitalsSkipped(Logger(scope));
            },
            (options, root) =>
            {
                var minutes = Read(root, VitalsMinutesKey, 5);
                options.Interval = minutes <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(minutes);
                options.Cadence = PeriodicJobCadence.FixedRate;
                options.LogExceptionDetails = false;
            });

    /// <summary>Observes durable submissions independently of the browser (<see cref="DeploymentFollowing"/>): a pass every
    /// <see cref="FollowSecondsKey"/> seconds reads the runner while a submission is unfinished, else every
    /// <see cref="FollowIdleSecondsKey"/> seconds; a deploy, control or reconcile the console submits wakes it. The runner
    /// reads target journals and keeps its observation schedule; this pass never starts, retries or reconciles a
    /// deployment, and shutdown cancels the observation only.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddDeploymentFollowingJob(this IServiceCollection services)
    {
        services.TryAddSingleton<DeploymentFollowSchedule>();
        services.TryAddScoped<DeploymentFollowing>();
        return services.AddPeriodicJob(
            FollowJob,
            (scope, ct) => scope.GetRequiredService<DeploymentFollowing>()
                .PassAsync(Idle(scope, FollowIdleSecondsKey), ct),
            (options, root) => options.Delay(Read(root, FollowSecondsKey, 10), 1, 300));
    }

    /// <summary>Runs the automation passes every <see cref="AutomationSecondsKey"/> seconds: the watch windows (each
    /// <see cref="WatchMinutesKey"/> long), the release watcher, auto-promote, the automatic deploy queue and the daily
    /// retention.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddDeploymentAutomationJob(this IServiceCollection services) =>
        services.AddPeriodicJob(
            AutomationJob,
            (scope, ct) => scope.GetRequiredService<DeploymentAutomationService>()
                .RunOnceAsync(ct, Math.Clamp(Read(scope, WatchMinutesKey, 10), 0, 120)),
            (options, root) => options.Delay(Read(root, AutomationSecondsKey, 120), 10, 3600));

    /// <summary>Moves the running fleet rollout every <see cref="FleetSecondsKey"/> seconds: records the finished outcome of
    /// the product it is on and submits the next. A pass with no running fleet reads one row and nothing else; shutdown
    /// pauses the fleet and the next start continues it.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddFleetRolloutJob(this IServiceCollection services) =>
        services.AddPeriodicJob<FleetRolloutService>(
            FleetJob,
            (fleet, ct) => fleet.AdvanceRunningAsync(ct),
            (options, root) => options.Delay(Read(root, FleetSecondsKey, 10), 2, 300));

    /// <summary>Runs the scheduled settings applies that are due every <see cref="ApplySecondsKey"/> seconds; each acts for
    /// the owner who scheduled it.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddSettingsApplyJob(this IServiceCollection services) =>
        services.AddPeriodicJob<SettingsApplyService>(
            ApplyJob,
            (applies, ct) => applies.RunDueAsync(ct),
            (options, root) => options.Delay(Read(root, ApplySecondsKey, 60), 10, 600));

    /// <summary>Records finished enrollments, placements and upkeeps (<see cref="InfraJobRecorder.FollowAsync"/>): each
    /// lands in the audit trail as its owner, and an enrolled server moves to its tailnet address. A pass every
    /// <see cref="InfraFollowSecondsKey"/> seconds reads the runner while a job is unfinished, else every
    /// <see cref="InfraFollowIdleSecondsKey"/> seconds; a job the console starts wakes it. A console that may not drive
    /// infrastructure (deployed dev) runs no pass: no job can exist there. The jobs themselves run detached and never
    /// depend on this pass.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddInfraFollowingJob(this IServiceCollection services)
    {
        services.TryAddSingleton<InfraFollowSchedule>();
        return services.AddPeriodicJob(
            InfraFollowJob,
            (scope, ct) => scope.GetRequiredService<InfraJobRecorder>()
                .FollowAsync(Idle(scope, InfraFollowIdleSecondsKey), ct),
            (options, root) => options.Delay(
                root.GetService<IInfraAccess>()?.Refusal is null ? Read(root, InfraFollowSecondsKey, 10) : 0, 2, 300));
    }

    /// <summary>Sets a fixed delay of <paramref name="seconds"/> within its bounds, or turns the job off at zero or less, and
    /// logs a failed pass by its type only.</summary>
    private static void Delay(this PeriodicJobOptions options, int seconds, int min, int max)
    {
        options.Interval = seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Clamp(seconds, min, max));
        options.Cadence = PeriodicJobCadence.FixedDelay;
        options.LogExceptionDetails = false;
    }

    private static int Read(IServiceProvider services, string key, int fallback) =>
        services.GetRequiredService<IConfiguration>().GetValue(key, fallback);

    /// <summary>A follower's idle interval: the configured seconds (default 300) within 30–3600.</summary>
    private static TimeSpan Idle(IServiceProvider services, string key) =>
        TimeSpan.FromSeconds(Math.Clamp(Read(services, key, 300), 30, 3600));

    private static ILogger Logger(IServiceProvider scope) =>
        scope.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PeriodicJobsServiceCollectionExtensions));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vitals sampling skipped: the runner could not read the fleet")]
    private static partial void LogVitalsSkipped(ILogger logger);

}
