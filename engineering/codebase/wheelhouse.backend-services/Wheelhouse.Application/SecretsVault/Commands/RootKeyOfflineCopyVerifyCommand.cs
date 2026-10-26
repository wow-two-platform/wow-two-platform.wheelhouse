using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Marks the owner's offline copy of the root key verified: they printed its fingerprint on their own machine and
/// it matched the key in use's. The audit entry keeps the reason; neither it nor the fingerprint holds the key.</summary>
/// <param name="Fingerprint">The key in use's fingerprint, as the console showed it to the owner.</param>
/// <param name="Reason">How the owner checked it, such as which copy they compared.</param>
/// <param name="Actor">Who marks it.</param>
public sealed record RootKeyOfflineCopyVerifyCommand(string Fingerprint, string Reason, string Actor)
    : ICommand<AppResult<RootKeyOfflineCopyDto>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "vault.offline-copy.verify";

    /// <inheritdoc />
    public string AuditSubject => "root-key";

    /// <inheritdoc />
    public string? AuditDetail => string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
}
