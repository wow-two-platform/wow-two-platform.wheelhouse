using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Records deployment requests without contacting a real server.</summary>
public sealed class StubDeploymentGateway : IDeploymentGateway
{
    public string? LastTarget { get; private set; }
    public string? LastRelease { get; private set; }
    public string? LastJob { get; private set; }
    public string? LastActor { get; private set; }
    public string? LastConfirm { get; private set; }
    public bool LastSkipTestPass { get; private set; }
    public (string Product, string Value)? LastBuild { get; private set; }
    public (string Product, string Branch)? LastCommits { get; private set; }

    public (string Resource, string? Id)? LastRead { get; private set; }
    public (string Target, string Service, int Tail)? LastLogs { get; private set; }

    /// <summary>When set, the next starts fail with this runner refusal, as a gate would refuse them.</summary>
    public string? StartRefusal { get; set; }

    public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct)
    {
        LastRead = (resource, id);
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement<object>(resource switch
        {
            "servers" => new object[] { new { id = "pilot-host", name = "Pilot", provider = "Hetzner", host = "vps.example.net", region = "hel1", sshUser = "deploy" } },
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

    public Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipTestPass, CancellationToken ct)
    {
        LastTarget = target;
        LastRelease = release;
        LastConfirm = confirm;
        LastSkipTestPass = skipTestPass;
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
