using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Starts the build workflow of one of a product's repositories (<paramref name="Repository"/>, else its primary
/// one) for a commit that has no build yet.</summary>
public sealed record DeploymentBuildCommand(string Product, string Commit, string Actor, string? Repository = null)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "build.request";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string AuditDetail => "commit " + Commit + (Repository is null ? "" : " in " + Repository);
}
