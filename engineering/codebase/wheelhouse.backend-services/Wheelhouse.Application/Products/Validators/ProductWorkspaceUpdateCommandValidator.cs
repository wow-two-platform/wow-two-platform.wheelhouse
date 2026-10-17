using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request to move a product into another workspace.</summary>
public sealed class ProductWorkspaceUpdateCommandValidator : AbstractValidator<ProductWorkspaceUpdateCommand>
{
    /// <summary>Configures the workspace-move rules.</summary>
    public ProductWorkspaceUpdateCommandValidator()
    {
        RuleFor(x => x.Product).MustBeProductKey();
        RuleFor(x => x.WorkspaceId).NotEmpty();
    }
}
