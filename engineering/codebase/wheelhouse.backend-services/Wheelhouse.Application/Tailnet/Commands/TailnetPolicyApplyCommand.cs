using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.Commands;

/// <summary>Applies the tailnet policy the owner reviewed.</summary>
/// <param name="Etag">The live policy's ETag the review showed; a policy changed since is refused.</param>
/// <param name="Digest">The rendered policy's SHA-256 the review showed; an inventory changed since is refused.</param>
/// <param name="Actor">Who applies it.</param>
public sealed record TailnetPolicyApplyCommand(string Etag, string Digest, string Actor)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "tailnet.policy.apply";

    /// <inheritdoc />
    public string AuditSubject => "tailnet";

    /// <inheritdoc />
    public string AuditDetail => "policy " + (Digest.Length >= 12 ? Digest[..12] : Digest);
}
