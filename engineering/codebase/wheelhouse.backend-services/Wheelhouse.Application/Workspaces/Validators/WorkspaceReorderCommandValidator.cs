using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Workspaces.Commands;

namespace Wheelhouse.Application.Workspaces.Validators;

/// <summary>Validates a request to reorder the workspaces: a list naming each one once.</summary>
public sealed class WorkspaceReorderCommandValidator : AbstractValidator<WorkspaceReorderCommand>
{
    /// <summary>Configures the reorder rules.</summary>
    public WorkspaceReorderCommandValidator()
    {
        RuleFor(x => x.Ids).MustListEachOnce();
    }
}
