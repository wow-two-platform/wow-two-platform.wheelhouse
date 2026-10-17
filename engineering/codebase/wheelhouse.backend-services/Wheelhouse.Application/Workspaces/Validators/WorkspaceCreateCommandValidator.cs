using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Workspaces.Commands;

namespace Wheelhouse.Application.Workspaces.Validators;

/// <summary>Validates a request to add a workspace.</summary>
public sealed class WorkspaceCreateCommandValidator : AbstractValidator<WorkspaceCreateCommand>
{
    /// <summary>Configures the workspace creation rules.</summary>
    public WorkspaceCreateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Description).MaximumLength(200);
    }
}
