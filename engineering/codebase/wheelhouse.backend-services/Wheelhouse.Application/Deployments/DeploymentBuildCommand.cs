using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Starts a product's build workflow for a commit that has no build yet.</summary>
public sealed record DeploymentBuildCommand(string Product, string Commit, string Actor)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "build.request";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string AuditDetail => "commit " + Commit;
}
