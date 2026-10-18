using System.Text.Json;
using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Submits one reviewed release to one configured target; <paramref name="Confirm"/> is the typed target ID,
/// <paramref name="SkipDevPass"/> lets prod take a release that has not succeeded on dev, and <paramref name="Cause"/>
/// names why the control plane deploys it on its own (null for an owner's deploy).</summary>
public sealed record DeploymentStartCommand(
    string Target, string Release, string Actor, string? Confirm = null, bool SkipDevPass = false,
    DeploymentCause? Cause = null)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "deployment.start";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Release + (SkipDevPass ? ", without a dev pass" : "") + Cause switch
    {
        DeploymentCause.AutoDev => ", automatic: a new release",
        DeploymentCause.AutoPromote => ", automatic: promoted after its dev watch",
        _ => "",
    };
}
