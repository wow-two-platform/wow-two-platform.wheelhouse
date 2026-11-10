using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Links a target's setting key to another product's key in the same environment: each deploy of the target's
/// product environment renders the source's current value under the key, over a value of its own, so a secret one
/// product generates reaches another unchanged and is never shown. Replaces the key's earlier link. The audit names both
/// keys, never a value.</summary>
/// <param name="Target">The target whose product environment the key belongs to.</param>
/// <param name="Key">The linking key.</param>
/// <param name="SourceTarget">A target of the other product, in the same environment.</param>
/// <param name="SourceKey">The source key in that environment.</param>
/// <param name="Actor">Who links it.</param>
public sealed record SettingLinkSetCommand(string Target, string Key, string SourceTarget, string SourceKey, string Actor)
    : ICommand<AppResult<SettingLinkDto>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "settings.link";

    /// <inheritdoc />
    public string AuditSubject => Target + "/" + Key;

    /// <inheritdoc />
    public string AuditDetail => SourceTarget + "/" + SourceKey;
}
