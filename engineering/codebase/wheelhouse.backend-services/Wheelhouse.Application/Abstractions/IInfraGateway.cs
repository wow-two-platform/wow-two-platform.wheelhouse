using System.Text.Json;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Exposes the runner's infrastructure actions: Tailscale through the owner's OAuth client, a server's
/// enrollment, a product's placement on a server and an enrolled server's upkeep. Answers are the runner's JSON, passed
/// through; none holds a secret.</summary>
public interface IInfraGateway
{
    /// <summary>Reads whether the OAuth client is in place and works, and each server's tailnet node.</summary>
    Task<AppResult<JsonElement>> TailnetAsync(CancellationToken ct);

    /// <summary>Reads the live tailnet policy beside the one Wheelhouse renders, their diff and Tailscale's validation.</summary>
    Task<AppResult<JsonElement>> PolicyAsync(CancellationToken ct);

    /// <summary>Applies the rendered policy the owner reviewed: its digest, while the live policy keeps its ETag.</summary>
    Task<AppResult<JsonElement>> ApplyPolicyAsync(string etag, string digest, string actor, CancellationToken ct);

    /// <summary>Puts back the policy that was live before Wheelhouse's last change, while the live one keeps its ETag.</summary>
    Task<AppResult<JsonElement>> RevertPolicyAsync(string etag, string actor, CancellationToken ct);

    /// <summary>Reads a server's enrollment with its live steps, and the one-time public key while the host needs it.</summary>
    Task<AppResult<JsonElement>> EnrollmentAsync(string server, CancellationToken ct);

    /// <summary>Creates a server's one-time enrollment key once and returns its public half.</summary>
    Task<AppResult<JsonElement>> EnrollmentKeyAsync(string server, CancellationToken ct);

    /// <summary>Starts, or reruns from its first unfinished step, a server's enrollment as a detached job.</summary>
    Task<AppResult<JsonElement>> EnrollAsync(ServerEnrollmentRequest request, string actor, CancellationToken ct);

    /// <summary>Reads a product's placements on servers, newest first.</summary>
    Task<AppResult<JsonElement>> PlacementsAsync(string product, CancellationToken ct);

    /// <summary>Starts placing a product's databases and private sites on a server as a detached job.</summary>
    Task<AppResult<JsonElement>> PlaceAsync(ProductPlacementRequest request, string actor, CancellationToken ct);

    /// <summary>Reads a server's upkeep runs, newest first, beside the digest of the kit this console ships.</summary>
    Task<AppResult<JsonElement>> UpkeepsAsync(string server, CancellationToken ct);

    /// <summary>Starts bringing an enrolled server to this console's kit as a detached job: the kit, its firewall
    /// (80 and 443 public) and the platform, then a check of the public entry points.</summary>
    Task<AppResult<JsonElement>> UpkeepAsync(string server, string actor, CancellationToken ct);

    /// <summary>Reads finished infrastructure jobs the console has not recorded yet (<c>jobs</c>) and how many are still
    /// queued or running (<c>open</c>).</summary>
    Task<AppResult<JsonElement>> FinishedAsync(CancellationToken ct);

    /// <summary>Marks a finished job recorded, so it is recorded once.</summary>
    Task<AppResult<JsonElement>> AcknowledgeAsync(string job, CancellationToken ct);
}
