using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.Commands;

/// <summary>Puts back the tailnet policy that was live before Wheelhouse's last change.</summary>
/// <param name="Etag">The live policy's ETag the owner saw; a policy changed since is refused.</param>
/// <param name="Actor">Who reverts it.</param>
public sealed record TailnetPolicyRevertCommand(string Etag, string Actor) : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "tailnet.policy.revert";

    /// <inheritdoc />
    public string AuditSubject => "tailnet";

    /// <inheritdoc />
    public string? AuditDetail => null;
}
