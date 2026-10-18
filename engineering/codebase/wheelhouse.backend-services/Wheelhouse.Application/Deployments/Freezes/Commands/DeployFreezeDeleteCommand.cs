using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Freezes.Commands;

/// <summary>Represents a command to lift the hold on one scope; lifting a scope that is not frozen changes nothing.</summary>
public sealed record DeployFreezeDeleteCommand : ICommand<AppResult<bool>>, IAuditedCommand
{
    /// <summary>Gets the product's identifier or slug, or null for the freeze of every product.</summary>
    public string? Product { get; init; }

    /// <summary>Gets <c>dev</c> or <c>prod</c> for the freeze of one environment of the product, or null.</summary>
    public string? Environment { get; init; }

    /// <inheritdoc />
    public string AuditAction => "deployment.unfreeze";

    /// <inheritdoc />
    public string AuditSubject => FreezeScope.Subject(Product, Environment);
}
