using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;

namespace Wheelhouse.Application.Deployments.History.Services;

/// <summary>Pure rules that read a deployment into the status taxonomy: what the environment runs because of it (the
/// state), which way it got there (the sub-state), how much attention it needs, why (the cause, with its origin), what
/// else it likely hurt (the effects) and how each service ended.</summary>
/// <remarks>
/// <para>From the runner's statuses: <c>succeeded</c> is Succeeded, or Degraded with warnings, unhealthy services or a
/// regression in its watch window; <c>failed</c> before containers changed is Not applied · check failed, after it
/// Partial (some services on the new release, some not) or Failed; <c>rolled_back</c> is Rolled back;
/// <c>rollback_failed</c> is Partial · partly rolled back when some services were restored, else Failed;
/// <c>interrupted</c> is Failed after changes and Not applied before them; <c>rejected</c> is Not applied · refused.
/// A deploy that never reached its target reads from its intent.</para>
/// <para>Origins and effects follow the architecture's honesty rule: a service is an effect of another only when it
/// declares a path to it (<c>depends_on</c>, directly or through others) and settled unhealthy after it; otherwise
/// each failing service is an origin of its own, never a guessed edge.</para>
/// </remarks>
public static class DeploymentStateRules
{
    /// <summary>The runner's statuses of a submission that has not finished.</summary>
    private static readonly HashSet<string> Pending = ["submitting", "queued", "running"];

    /// <summary>The health readings that say nothing is wrong: <c>unknown</c> is no container, still starting, or a
    /// rollout that stopped before changing anything.</summary>
    private static readonly HashSet<string> Fine = ["healthy", "starting", "running", "unknown"];

    /// <summary>The steps before a rollout changes anything.</summary>
    private static readonly HashSet<string> Checking = ["Render settings", "Check target", "Snapshot data", "Pull images", "Dry-run migrations"];

    /// <summary>The steps after the services started.</summary>
    private static readonly HashSet<string> Verifying = ["Verify services", "Publish sites", "Probe sites", "Verify end to end"];

    /// <summary>Reads a deployment into the taxonomy.</summary>
    /// <param name="record">The runner's record, or one the history built from an intent.</param>
    /// <param name="watch">The watch after a successful rollout, when it was watched.</param>
    /// <param name="intent">The control plane's record of a deploy that never reached its target.</param>
    /// <param name="firstDeploy">Whether the target had no success before it, so nothing could be rolled back to.</param>
    /// <returns>The reading.</returns>
    public static DeploymentStateReading Of(
        DeploymentRecord record, DeploymentWatchDto? watch, DeploymentIntentEntity? intent, bool firstDeploy = false)
    {
        if (intent is not null)
            return OfIntent(intent);
        if (record.Operation is not null)
            return OfControl(record);

        var pending = Pending.Contains(record.Status);
        var found = watch is { Status: "degraded" } ? Named(watch.Findings, record.ServiceRollout) : [];
        var services = Results(record, pending, found);
        var (origins, effects) = Trace(record.ServiceRollout, services);
        if (services is not null)
            services = [.. services.Select(item => origins.Contains(item.Service) ? item with { Origin = true } : item)];

        DeploymentStateReading Reading(string state, string? substate, string attention, DeploymentCauseDto? cause = null) =>
            new(state, substate, attention, cause, effects, services);

        switch (record.Status)
        {
            case "submitting" or "queued":
                return Reading(DeploymentState.InProgress, "queued", DeploymentAttention.Info);
            case "running":
                return Reading(DeploymentState.InProgress, StepSubstate(record.CurrentStep?.Name), DeploymentAttention.Info);
            case "succeeded":
                return Succeeded(record, watch, services, found, Reading);
            case "failed" when record.MutationStarted != true:
                return Reading(DeploymentState.NotApplied, "check_failed", DeploymentAttention.Look,
                    new DeploymentCauseDto("step", record.Reason?.StartsWith("Snapshot", StringComparison.Ordinal) == true ? "Snapshot data" : null,
                        record.Reason ?? "A check before the rollout failed; nothing changed.", record.CompletedAt));
            case "failed":
            {
                var cause = FailureCause(record, services, origins);
                if (Has(services, "updated", "new") && Has(services, "failed", "not_reached"))
                    return Reading(DeploymentState.Partial, "stopped_mid_rollout", AllHealthy(services) ? DeploymentAttention.Look : DeploymentAttention.Act,
                        cause);
                return Reading(DeploymentState.Failed, firstDeploy ? "first_deploy_unhealthy" : "rollback_not_allowed", DeploymentAttention.Act, cause);
            }
            case "rolled_back":
                return Reading(DeploymentState.RolledBack,
                    services?.Count(item => item.Result == "restored") > 1 ? "together" : "automatic", DeploymentAttention.Look,
                    FailureCause(record, services, origins));
            case "rollback_failed":
                return Has(services, "restored") && Has(services, "failed", "updated", "new", "affected")
                    ? Reading(DeploymentState.Partial, "partly_rolled_back", AllHealthy(services) ? DeploymentAttention.Look : DeploymentAttention.Act,
                        FailureCause(record, services, origins))
                    : Reading(DeploymentState.Failed, "rollback_failed", DeploymentAttention.Act, FailureCause(record, services, origins));
            case "interrupted" when record.MutationStarted == true:
                return Reading(DeploymentState.Failed, "interrupted", DeploymentAttention.Act,
                    new DeploymentCauseDto("runner", null, record.Reason ?? "The rollout stopped mid-way and was reconciled.", record.CompletedAt));
            case "interrupted":
                return Reading(DeploymentState.NotApplied, "interrupted", DeploymentAttention.Look,
                    new DeploymentCauseDto("runner", null, record.Reason ?? "The rollout stopped before it changed anything.", record.CompletedAt));
            case "rejected":
                return Rejected(record, Reading);
            default:
                return Reading(DeploymentState.Unknown, record.Status == "unknown" ? "no_receipt" : "unreadable", DeploymentAttention.Muted);
        }
    }

    /// <summary>A control — a stop, start or restart of the release a target runs — read into the taxonomy: it changes
    /// no release, so it reads as in progress, succeeded or failed, or as not applied when the runner refused it.</summary>
    /// <param name="record">The control's record.</param>
    /// <returns>The reading.</returns>
    public static DeploymentStateReading OfControl(DeploymentRecord record)
    {
        DeploymentStateReading Reading(string state, string? substate, string attention, DeploymentCauseDto? cause = null) =>
            new(state, substate, attention, cause, [], null);
        var step = record.CurrentStep?.Name;
        return record.Status switch
        {
            "submitting" or "queued" => Reading(DeploymentState.InProgress, "queued", DeploymentAttention.Info),
            "running" => Reading(DeploymentState.InProgress, null, DeploymentAttention.Info),
            "succeeded" => Reading(DeploymentState.Succeeded, null, DeploymentAttention.None),
            "failed" => Reading(DeploymentState.Failed, null, DeploymentAttention.Act,
                new DeploymentCauseDto("step", step, record.Reason ?? "The control failed.", record.CompletedAt)),
            "interrupted" => Reading(DeploymentState.Failed, "interrupted", DeploymentAttention.Act,
                new DeploymentCauseDto("runner", null, record.Reason ?? "The control stopped mid-way and was reconciled.", record.CompletedAt)),
            "rejected" => Reading(DeploymentState.NotApplied, "refused", DeploymentAttention.Look,
                new DeploymentCauseDto("validation", null, record.Reason ?? "The target refused the control.", record.CompletedAt)),
            _ => Reading(DeploymentState.Unknown, record.Status == "unknown" ? "no_receipt" : "unreadable", DeploymentAttention.Muted),
        };
    }

    /// <summary>The sub-state a live rollout's step puts it in.</summary>
    /// <param name="step">The step's name, as the runner names it.</param>
    /// <returns><c>checking</c>, <c>draining</c>, <c>maintenance</c>, <c>migrating</c>, <c>starting</c>,
    /// <c>verifying</c>, <c>rolling_back</c> or <c>waiting</c>.</returns>
    public static string StepSubstate(string? step) => step switch
    {
        null => "checking",
        _ when Checking.Contains(step) => "checking",
        _ when Verifying.Contains(step) => "verifying",
        _ when step.StartsWith("Drain", StringComparison.Ordinal) => "draining",
        _ when step.StartsWith("Maintenance", StringComparison.Ordinal) => "maintenance",
        _ when step.StartsWith("Migrate", StringComparison.Ordinal) => "migrating",
        _ when step.StartsWith("Roll back", StringComparison.Ordinal) => "rolling_back",
        _ when step.StartsWith("Wait", StringComparison.Ordinal) => "waiting",
        _ => "starting",
    };

    /// <summary>A success: degraded with warnings, an unhealthy service or a regression while watched. @internal</summary>
    private static DeploymentStateReading Succeeded(
        DeploymentRecord record, DeploymentWatchDto? watch, IReadOnlyList<DeploymentServiceResultDto>? services,
        IReadOnlyCollection<string> found, Func<string, string?, string, DeploymentCauseDto?, DeploymentStateReading> reading)
    {
        if (watch is { Status: "degraded" })
        {
            var origin = services?.FirstOrDefault(item => item.Origin)?.Service ?? found.FirstOrDefault();
            // Only services the release left alone regressed: a side effect of the deploy, not the release itself.
            var sideEffect = found.Count > 0 && record.ServiceRollout is { } rollout
                && found.All(name => rollout.Services.FirstOrDefault(item => item.Name == name)?.Change == "unchanged");
            return reading(DeploymentState.Degraded, sideEffect ? "side_effect" : "in_watch", DeploymentAttention.Look,
                new DeploymentCauseDto(origin is null ? "platform" : "service", origin,
                    watch.Findings.FirstOrDefault() ?? "Regressed after the deploy.", watch.DegradedAt));
        }

        var unhealthy = services?.FirstOrDefault(item => item.Result == "affected");
        if (record.Warnings is [var warning, ..])
            return reading(DeploymentState.Degraded, "at_deploy", DeploymentAttention.Look,
                new DeploymentCauseDto("step",
                    warning.StartsWith("Site ", StringComparison.Ordinal) ? "Probe sites"
                    : warning.StartsWith("The end-to-end check", StringComparison.Ordinal) ? "Verify end to end" : null,
                    warning, record.CompletedAt));
        if (unhealthy is not null)
            return reading(DeploymentState.Degraded, "at_deploy", DeploymentAttention.Look,
                new DeploymentCauseDto("service", unhealthy.Service, unhealthy.Service + " is " + unhealthy.Health, record.CompletedAt));
        var substate = watch?.Status switch
        {
            "watching" or "passed" or "unverified" or "replaced" => watch.Status,
            "superseded" => "replaced",
            _ => null,
        };
        return reading(DeploymentState.Succeeded, substate, DeploymentAttention.None, null);
    }

    /// <summary>A refusal the runner wrote: refused, or a failed check when the target lacked the disk. @internal</summary>
    private static DeploymentStateReading Rejected(
        DeploymentRecord record, Func<string, string?, string, DeploymentCauseDto?, DeploymentStateReading> reading)
    {
        var reason = record.Reason;
        if (reason?.Contains("disk", StringComparison.OrdinalIgnoreCase) == true)
            return reading(DeploymentState.NotApplied, "check_failed", DeploymentAttention.Look,
                new DeploymentCauseDto("platform", "Check target", reason, record.CompletedAt));
        if (reason?.Contains("reconcil", StringComparison.OrdinalIgnoreCase) == true)
            return reading(DeploymentState.NotApplied, "refused", DeploymentAttention.Look,
                new DeploymentCauseDto("runner", "Check target", reason, record.CompletedAt));
        return reading(DeploymentState.NotApplied, "refused", DeploymentAttention.None,
            new DeploymentCauseDto("validation", null, reason ?? "The runner refused the release.", record.CompletedAt ?? record.SubmittedAt));
    }

    /// <summary>A deploy that never reached its target. @internal</summary>
    private static DeploymentStateReading OfIntent(DeploymentIntentEntity intent)
    {
        var (substate, attention, cause) = intent.Outcome switch
        {
            DeploymentIntentOutcome.Superseded => ("superseded", DeploymentAttention.None,
                new DeploymentCauseDto("platform", null,
                    intent.Message ?? (intent.ReplacedBy is { } newer ? "Replaced by " + newer + " while queued." : "Replaced while queued."), intent.At)),
            DeploymentIntentOutcome.Cancelled => ("cancelled", DeploymentAttention.None,
                new DeploymentCauseDto("operator", null, intent.Message ?? "Cancelled before it ran.", intent.At)),
            DeploymentIntentOutcome.Expired => ("expired", DeploymentAttention.Look,
                new DeploymentCauseDto("platform", null, intent.Message ?? "Its schedule passed before it could run.", intent.At)),
            DeploymentIntentOutcome.Aborted => ("aborted", DeploymentAttention.Look,
                new DeploymentCauseDto("busy", null, intent.Message ?? "The target stayed busy past the wait.", intent.At)),
            _ => ("refused", DeploymentAttention.None,
                new DeploymentCauseDto(intent.Refusal switch
                {
                    DeploymentRefusal.Freeze => "freeze",
                    DeploymentRefusal.Gate => "gate",
                    _ => "validation",
                }, null, intent.Message, intent.At)),
        };
        return new DeploymentStateReading(DeploymentState.NotApplied, substate, attention, cause, [], null);
    }

    /// <summary>Why a rollout failed: its origin service with the runner's reason, else the services the reason names as
    /// unhealthy, else the step. @internal</summary>
    private static DeploymentCauseDto FailureCause(
        DeploymentRecord record, IReadOnlyList<DeploymentServiceResultDto>? services, IReadOnlySet<string> origins)
    {
        var origin = services?.FirstOrDefault(item => origins.Contains(item.Service))?.Service ?? Unhealthy(record.Reason).FirstOrDefault();
        var at = origin is null ? null : record.ServiceRollout?.Services.FirstOrDefault(item => item.Name == origin)?.At;
        return new DeploymentCauseDto(origin is null ? "step" : "service", origin,
            record.Reason ?? (origin is null ? "The rollout failed." : origin + " did not become healthy."), at ?? record.CompletedAt);
    }

    /// <summary>Each service's result in rollout order: the waves first, then the services they left alone. @internal</summary>
    private static List<DeploymentServiceResultDto>? Results(DeploymentRecord record, bool pending, IReadOnlyCollection<string> found)
    {
        if (record.ServiceRollout is not { Services.Count: > 0 } rollout)
            return null;
        var order = rollout.Waves.SelectMany(wave => wave).Distinct(StringComparer.Ordinal).ToList();
        var starting = pending && record.CurrentStep?.Name is { } step && step.StartsWith("Start ", StringComparison.Ordinal)
            ? step["Start ".Length..].Split(", ").ToHashSet(StringComparer.Ordinal)
            : [];
        return
        [
            .. rollout.Services
                .Select((item, index) => (Item: item, Rank: order.IndexOf(item.Name) is var at and >= 0 ? at : order.Count + index))
                .OrderBy(pair => pair.Rank)
                .Select(pair =>
                {
                    var item = pair.Item;
                    var result = item.Outcome switch
                    {
                        "unchanged" => "unchanged",
                        "updated" => item.Change == "added" ? "new" : "updated",
                        "failed" => "failed",
                        "not_started" => "not_reached",
                        "rolled_back" => "restored",
                        "pending" when pending => starting.Contains(item.Name) ? "starting" : "waiting",
                        _ => "unknown",
                    };
                    var hurt = found.Contains(item.Name) || (item.Health is { } health && !Fine.Contains(health));
                    if (hurt && result is "updated" or "new" or "unchanged" or "restored")
                        result = "affected";
                    return new DeploymentServiceResultDto(item.Name, result, false, item.From, item.To, item.Health, item.Reason);
                }),
        ];
    }

    /// <summary>Which failing services are origins and which are effects: a failing service is an effect when it declares
    /// a path to a failing origin and settled after it; every other failing service is an origin of its own. @internal</summary>
    private static (IReadOnlySet<string> Origins, IReadOnlyList<DeploymentEffectDto> Effects) Trace(
        ServiceRolloutDto? rollout, IReadOnlyList<DeploymentServiceResultDto>? services)
    {
        var origins = new HashSet<string>(StringComparer.Ordinal);
        if (rollout is null || services is null)
            return (origins, []);
        var failed = services.Where(item => item.Result == "failed").Select(item => item.Service).ToList();
        var affected = services.Where(item => item.Result == "affected").Select(item => item.Service).ToList();
        // Without a failed service, the trouble starts at an affected one: a regression after the deploy.
        var candidates = failed.Count > 0 ? failed : affected;
        var byName = rollout.Services.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var effects = new List<DeploymentEffectDto>();
        foreach (var name in candidates)
            if (EffectOf(name, candidates, byName) is null)
                origins.Add(name);
        if (origins.Count == 0)
            origins.UnionWith(candidates);
        foreach (var name in failed.Concat(affected).Distinct(StringComparer.Ordinal).Where(name => !origins.Contains(name)))
            if (EffectOf(name, origins, byName) is { } via)
                effects.Add(new DeploymentEffectDto(name,
                    byName[name].Health is { } health && !Fine.Contains(health) ? health : "turned unhealthy", via, byName[name].At));
        return (origins, effects);
    }

    /// <summary>The origin a service is likely an effect of: one it declares a path to that settled before it; null when
    /// none qualifies. @internal</summary>
    private static string? EffectOf(string name, IEnumerable<string> origins, IReadOnlyDictionary<string, ServiceRolloutItemDto> byName)
    {
        if (!byName.TryGetValue(name, out var service) || service.At is not { } at)
            return null;
        var reach = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(service.DependsOn);
        while (queue.TryDequeue(out var next))
        {
            if (!reach.Add(next))
                continue;
            foreach (var further in byName.GetValueOrDefault(next)?.DependsOn ?? [])
                queue.Enqueue(further);
        }

        return origins.FirstOrDefault(origin => origin != name && reach.Contains(origin)
            && byName.GetValueOrDefault(origin)?.At is { } first && first < at);
    }

    /// <summary>The services a watch's findings name, matched against the rollout's services. @internal</summary>
    private static HashSet<string> Named(IReadOnlyList<string> findings, ServiceRolloutDto? rollout)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var space = finding.IndexOf(' ', StringComparison.Ordinal);
            var name = space > 0 ? finding[..space] : finding;
            if (name != "Site" && (rollout is null || rollout.Services.Any(item => item.Name == name)))
                named.Add(name);
        }

        return named;
    }

    /// <summary>The services a runner's reason names as not healthy: <c>…; not healthy: jobs (unhealthy), web
    /// (restarting)</c>. @internal</summary>
    private static IEnumerable<string> Unhealthy(string? reason)
    {
        const string marker = "not healthy: ";
        var at = reason?.LastIndexOf(marker, StringComparison.Ordinal) ?? -1;
        if (reason is null || at < 0)
            yield break;
        foreach (var part in reason[(at + marker.Length)..].Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            var open = part.IndexOf(" (", StringComparison.Ordinal);
            yield return open > 0 ? part[..open] : part;
        }
    }

    /// <summary>Whether any service ended in one of the results. @internal</summary>
    private static bool Has(IReadOnlyList<DeploymentServiceResultDto>? services, params string[] results) =>
        services?.Any(item => results.Contains(item.Result)) == true;

    /// <summary>Whether the target reported every service healthy. @internal</summary>
    private static bool AllHealthy(IReadOnlyList<DeploymentServiceResultDto>? services) =>
        services is { Count: > 0 } && services.All(item => item.Health == "healthy");
}
