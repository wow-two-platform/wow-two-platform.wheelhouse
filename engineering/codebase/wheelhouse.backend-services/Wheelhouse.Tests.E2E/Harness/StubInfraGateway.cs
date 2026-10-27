using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Stands in for the runner's infra.py: records every request and answers with what a test scripted, never
/// reaching Tailscale or a host.</summary>
public sealed class StubInfraGateway : IInfraGateway
{
    /// <summary>Every call, in order, as its action and arguments.</summary>
    public List<(string Action, object? Request, string? Actor)> Calls { get; } = [];

    /// <summary>The finished jobs the next <see cref="FinishedAsync"/> answers; tests add them.</summary>
    public List<object> Finished { get; } = [];

    /// <summary>How many jobs <see cref="FinishedAsync"/> answers are still queued or running.</summary>
    public int Open { get; set; }

    /// <summary>The job IDs acknowledged as recorded.</summary>
    public List<string> Acknowledged { get; } = [];

    /// <summary>When set, the next writes fail with this runner refusal.</summary>
    public string? Refusal { get; set; }

    /// <summary>Restores the defaults.</summary>
    public void Clear()
    {
        Calls.Clear();
        Finished.Clear();
        Open = 0;
        Acknowledged.Clear();
        Refusal = null;
    }

    public Task<AppResult<JsonElement>> TailnetAsync(CancellationToken ct) => Answer("tailnet", null, null, new
    {
        configured = true, connected = true, reason = (string?)null, tailnet = "tail1234.ts.net",
        clientFile = "providers/tailscale/oauth-client.json",
        nodes = new[] { new { id = "nvps", name = "ocharo-vps.tail1234.ts.net", hostname = "ocharo-vps", serverId = "prod-host",
            role = "host", tags = new[] { "tag:products" }, online = true } },
        servers = new[] { new { serverId = "prod-host", nodeId = "nvps" } },
    });

    public Task<AppResult<JsonElement>> PolicyAsync(CancellationToken ct) =>
        Answer("policy", null, null, new { etag = "\"e1\"", changed = true, renderedDigest = new string('a', 64) });

    public Task<AppResult<JsonElement>> ApplyPolicyAsync(string etag, string digest, string actor, CancellationToken ct) =>
        Write("policy-apply", (etag, digest), actor, new { applied = true, digest });

    public Task<AppResult<JsonElement>> RevertPolicyAsync(string etag, string actor, CancellationToken ct) =>
        Write("policy-revert", etag, actor, new { reverted = true });

    public Task<AppResult<JsonElement>> EnrollmentAsync(string server, CancellationToken ct) =>
        Answer("enrollment", server, null, new { kind = "enrollment", serverId = server, status = "not_started" });

    public Task<AppResult<JsonElement>> EnrollmentKeyAsync(string server, CancellationToken ct) =>
        Write("enrollment-key", server, null, new { serverId = server, publicKey = "ssh-ed25519 AAAA wheelhouse-enroll-" + server });

    public Task<AppResult<JsonElement>> EnrollAsync(ServerEnrollmentRequest request, string actor, CancellationToken ct) =>
        Write("enroll", request, actor, new { kind = "enrollment", serverId = request.Server, status = "queued" });

    public Task<AppResult<JsonElement>> PlacementsAsync(string product, CancellationToken ct) =>
        Answer("placements", product, null, Array.Empty<object>());

    public Task<AppResult<JsonElement>> PlaceAsync(ProductPlacementRequest request, string actor, CancellationToken ct) =>
        Write("place", request, actor, new { kind = "placement", product = request.Product, status = "queued" });

    public Task<AppResult<JsonElement>> UpkeepsAsync(string server, CancellationToken ct) =>
        Answer("upkeeps", server, null, new { serverId = server, kit = new string('c', 64), jobs = Array.Empty<object>() });

    public Task<AppResult<JsonElement>> UpkeepAsync(string server, string actor, CancellationToken ct) =>
        Write("upkeep", server, actor, new { id = Guid.NewGuid().ToString("D"), kind = "upkeep", serverId = server, status = "queued" });

    public Task<AppResult<JsonElement>> FinishedAsync(CancellationToken ct) =>
        Answer("finished", null, null, new { jobs = Finished.ToArray(), open = Open });

    public Task<AppResult<JsonElement>> AcknowledgeAsync(string job, CancellationToken ct)
    {
        Acknowledged.Add(job);
        Finished.RemoveAll(item => JsonSerializer.SerializeToElement(item).GetProperty("id").GetString() == job);
        return Answer("acknowledge", job, null, new { id = job, recorded = true });
    }

    private Task<AppResult<JsonElement>> Write(string action, object? request, string? actor, object answer) =>
        Refusal is { } refusal
            ? Task.FromResult(AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal)))
            : Answer(action, request, actor, answer);

    private Task<AppResult<JsonElement>> Answer(string action, object? request, string? actor, object answer)
    {
        Calls.Add((action, request, actor));
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(answer)));
    }
}
