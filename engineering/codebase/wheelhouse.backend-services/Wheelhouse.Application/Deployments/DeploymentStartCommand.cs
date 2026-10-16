using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Submits one reviewed release to one configured target; <paramref name="Confirm"/> is the typed target ID,
/// and <paramref name="SkipTestPass"/> lets prod take a release that has not succeeded on test.</summary>
public sealed record DeploymentStartCommand(
    string Target, string Release, string Actor, string? Confirm = null, bool SkipTestPass = false)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "deployment.start";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Release + (SkipTestPass ? ", without a test pass" : "");
}
