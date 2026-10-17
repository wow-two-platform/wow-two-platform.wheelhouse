using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request to mark a product active or inactive.</summary>
public sealed class ProductStatusUpdateCommandValidator : AbstractValidator<ProductStatusUpdateCommand>
{
    /// <summary>Configures the status-update rules.</summary>
    public ProductStatusUpdateCommandValidator()
    {
        RuleFor(x => x.Product).MustBeProductKey();
        RuleFor(x => x.Status).IsInEnum();
    }
}
