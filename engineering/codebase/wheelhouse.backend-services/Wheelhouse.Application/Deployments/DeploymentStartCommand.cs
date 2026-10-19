using System.Text.Json;
using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Submits one reviewed release to one configured target; <paramref name="Confirm"/> is the typed target ID,
/// <paramref name="SkipDevPass"/> lets prod take a release that has not succeeded on dev, <paramref name="Reason"/> is the
/// owner's one-line why (stored on the job), <paramref name="OverrideFreeze"/> deploys although a freeze holds the target
/// (it needs a reason), <paramref name="Cause"/> names why the control plane deploys it on its own (null for an
/// owner's deploy), <paramref name="Fleet"/> names the fleet rollout that submits it, and <paramref name="ReleaseLabel"/>
/// is the release's label when the caller knows it, so a refusal reads by it.</summary>
public sealed record DeploymentStartCommand(
    string Target, string Release, string Actor, string? Confirm = null, bool SkipDevPass = false,
    DeploymentCause? Cause = null, string? Reason = null, bool OverrideFreeze = false, Guid? Fleet = null,
    string? ReleaseLabel = null)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "deployment.start";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Release + (SkipDevPass ? ", without a dev pass" : "")
        + (OverrideFreeze ? ", overriding a freeze" : "") + Cause switch
        {
            DeploymentCause.AutoDev => ", automatic: a new release",
            DeploymentCause.AutoPromote => ", automatic: promoted after its dev watch",
            _ => "",
        } + (Fleet is { } fleet ? ", fleet " + fleet : "")
        + (string.IsNullOrWhiteSpace(Reason) ? "" : ", reason: " + Reason.Trim());
}
