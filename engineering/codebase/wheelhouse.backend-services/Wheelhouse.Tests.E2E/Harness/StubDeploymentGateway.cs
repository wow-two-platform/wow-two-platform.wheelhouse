using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Records deployment requests without contacting a real server.</summary>
public sealed class StubDeploymentGateway : IDeploymentGateway
{
    public string? LastTarget { get; private set; }
    public string? LastRelease { get; private set; }
    public string? LastJob { get; private set; }
    public string? LastActor { get; private set; }
    public string? LastConfirm { get; private set; }
    public bool LastSkipDevPass { get; private set; }
    public string? LastReason { get; private set; }
    public (string Product, string Value)? LastBuild { get; private set; }
    public (string Product, string Branch)? LastCommits { get; private set; }

    public (string Resource, string? Id)? LastRead { get; private set; }
    public (string Target, string Service, int Tail)? LastLogs { get; private set; }

    /// <summary>When set, the next starts fail with this runner refusal, as a gate would refuse them.</summary>
    public string? StartRefusal { get; set; }

    /// <summary>When set, reads and starts go to this gateway instead, such as the real runner over a test root.</summary>
    public IDeploymentGateway? Runner { get; set; }

    /// <summary>When set, reads fail with this error, as a timed-out, unreachable or refusing runner would.</summary>
    public AppError? ReadFailure { get; set; }

    /// <summary>When set, reads throw this exception, as a defect would.</summary>
    public Exception? ReadException { get; set; }

    /// <summary>The runner's history projection the <c>history</c> read answers; tests add records to it.</summary>
    public List<object> History { get; } = [];

    /// <summary>What the release watcher's <c>latest</c> read answers: each product's newest release; tests add them.</summary>
    public List<object> Latest { get; } = [];

    /// <summary>Every submission, in order: target, release (bundle) and actor.</summary>
    public List<(string Target, string Release, string Actor)> Starts { get; } = [];

    /// <summary>The local observation each <c>observation</c> read answers, by submission id; a missing one is refused.</summary>
    public Dictionary<string, object> Observations { get; } = [];

    public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
    {
        LastRead = (resource, id);
        if (Runner is not null)
            return Runner.ReadAsync(resource, id, ct);
        if (ReadException is not null)
            throw ReadException;
        if (ReadFailure is not null)
            return Task.FromResult(AppResult<JsonElement>.Fail(ReadFailure));
        if (resource == "observation")
            return Task.FromResult(Observations.TryGetValue(id!, out var observation)
                ? AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(observation))
                : AppResult<JsonElement>.Fail(AppErrorFactory.Conflict("Deployment rejected: Unknown deployment")));
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement<object>(resource switch
        {
            "servers" => new object[] { new { id = "pilot-host", name = "Pilot", provider = "Hetzner", host = "vps.example.net", region = "hel1", sshUser = "deploy" } },
            "history" => History.ToArray(),
            "latest" => Latest.ToArray(),
            "jobs" => new object[] { new { id = Guid.Empty, targetId = "pilot", release = "v1", status = "failed", reason = "compose up failed (exit 1)" } },
            "state" => new { targetId = id, condition = "needs_reconciliation", active = new { id = Guid.Empty, status = "failed" } },
            "topology" => new { targetId = id, availability = "available", release = "v1", collectedAt = "2026-09-26T12:00:00+00:00",
                services = new[] { new { name = "api", image = (string?)null, networks = new[] { "default" }, volumes = Array.Empty<string>(), ports = new[] { "8080/tcp" } } },
                networks = new[] { new { name = "default", external = false } }, volumes = Array.Empty<object>(),
                dependencies = Array.Empty<object>(), warnings = Array.Empty<string>() },
            "stats" => new { windowDays = int.Parse(id!), deploys = 1, succeeded = 0, failed = 1, refused = 0, pending = 0 },
            "vitals" => new { collectedAt = "2026-09-26T12:00:00+00:00", targets = new[] { new { targetId = "pilot", serverId = "pilot-host", ok = true } } },
            "branches" => new[] { "main", "feature/pins" },
            "sites" => new Dictionary<string, object[]>
            {
                ["foreverpin-dev"] = [new { name = "app", url = "https://dev.foreverpin.example", exposure = "public" }],
            },
            _ => new object[] { new { id = "pilot", product = "foreverpin", environment = "dev", acceptsCandidates = true, needsConfirmation = false } }
        })));
    }

    public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct)
    {
        LastTarget = target;
        LastRelease = release;
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(
            new { targetId = target, ok = false, checks = new[] { new { name = "Settings: management", ok = false, detail = "Missing required setting: management:Billing:SecretKey" } } })));
    }

    public Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, CancellationToken ct)
    {
        LastReason = reason;
        LastTarget = target;
        LastRelease = release;
        LastActor = actor;
        LastConfirm = confirm;
        LastSkipDevPass = skipDevPass;
        Starts.Add((target, release, actor));
        if (Runner is not null)
            return Runner.StartAsync(target, release, actor, confirm, skipDevPass, reason, ct);
        if (StartRefusal is not null)
            return Task.FromResult(AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(StartRefusal)));
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(
            new { id = Guid.NewGuid(), status = "queued" })));
    }

    public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct)
    {
        LastCommits = (product, branch);
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new[]
        {
            new { sha = new string('c', 40), message = "feat: pins", author = "Max", date = "2026-09-27T09:00:00Z", buildId = (string?)"foreverpin-ci-7" },
            new { sha = new string('e', 40), message = "fix: other", author = "Max", date = "2026-09-27T08:00:00Z", buildId = (string?)null }
        })));
    }

    public (string Product, string? From, string To)? LastChangelog { get; private set; }

    public Task<AppResult<JsonElement>> ChangelogAsync(string product, string? from, string to, CancellationToken ct)
    {
        LastChangelog = (product, from, to);
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new
        {
            product, from, to, releases = new[] { new { release = to, name = to, publishedAt = "2026-10-02T10:00:00Z", body = "- fix: login", truncated = false, url = "https://github.com/o/p/releases/tag/" + to } },
            commits = Array.Empty<object>(), totalCommits = 0, migrations = Array.Empty<string>(), compareUrl = (string?)null, reason = (string?)null,
        })));
    }

    public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct)
    {
        LastBuild = (product, commit);
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(
            new { product, commit, status = "requested" })));
    }

    public Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct)
    {
        LastLogs = (target, service, tail);
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new
        {
            project = "foreverpin-dev", service, tail, collectedAt = "2026-09-28T10:00:00+00:00",
            lines = new[] { "2026-09-28T10:00:00Z started" }, truncated = false, targetId = target
        })));
    }

    public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct)
    {
        LastTarget = target;
        LastJob = job;
        LastActor = actor;
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(
            new { id = job, status = "interrupted", reconciledBy = actor })));
    }
}
