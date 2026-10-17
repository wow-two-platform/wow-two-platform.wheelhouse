using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Workspaces.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.Commands;

/// <summary>Represents a command to add a workspace; it takes the last place in the order.</summary>
public sealed record WorkspaceCreateCommand : ICommand<AppResult<WorkspaceDto>>, IAuditedCommand
{
    /// <summary>Gets the name the catalog knows the workspace by; fixed once created.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }

    /// <inheritdoc />
    public string AuditAction => "workspace.create";

    /// <inheritdoc />
    public string AuditSubject => Slug;
}
