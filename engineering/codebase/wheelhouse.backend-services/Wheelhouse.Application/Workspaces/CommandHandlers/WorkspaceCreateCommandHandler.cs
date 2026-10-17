using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Workspaces.Commands;
using Wheelhouse.Application.Workspaces.Mappers;
using Wheelhouse.Application.Workspaces.Models;
using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.CommandHandlers;

/// <summary>Handles <see cref="WorkspaceCreateCommand"/>; a new workspace comes last.</summary>
public sealed class WorkspaceCreateCommandHandler(IWorkspacesRepository workspaces)
    : ICommandHandler<WorkspaceCreateCommand, AppResult<WorkspaceDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<WorkspaceDto>> HandleAsync(WorkspaceCreateCommand request, CancellationToken cancellationToken)
    {
        if (await workspaces.GetBySlugAsync(request.Slug, cancellationToken) is not null)
            return AppResult<WorkspaceDto>.Fail(AppErrorFactory.Conflict($"A workspace is already named '{request.Slug}'."));

        var workspace = await workspaces.CreateAsync(new WorkspaceEntity
        {
            Id = Guid.CreateVersion7(),
            Slug = request.Slug,
            Name = request.Name.Trim(),
            Description = WorkspaceText.Description(request.Description),
            Order = await workspaces.NextOrderAsync(cancellationToken),
        }, cancellationToken);
        return AppResult<WorkspaceDto>.Ok(WorkspaceMapper.Map(workspace));
    }
}
