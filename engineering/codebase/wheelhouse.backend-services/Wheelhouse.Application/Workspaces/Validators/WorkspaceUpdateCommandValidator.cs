using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Workspaces.Commands;

namespace Wheelhouse.Application.Workspaces.Validators;

/// <summary>Validates a request to rename or redescribe a workspace.</summary>
public sealed class WorkspaceUpdateCommandValidator : AbstractValidator<WorkspaceUpdateCommand>
{
    /// <summary>Configures the workspace update rules.</summary>
    public WorkspaceUpdateCommandValidator()
    {
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Description).MaximumLength(200);
    }
}
