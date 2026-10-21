using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates one further repository of a product: an <c>owner/name</c> and, when it has one, its release source.</summary>
public sealed class ProductComponentValueObjectValidator : AbstractValidator<ProductComponentValueObject>
{
    /// <summary>Configures the further repository rules.</summary>
    public ProductComponentValueObjectValidator()
    {
        RuleFor(x => x.Repository).MustMatch(InventoryPatternConstants.Repository, "a GitHub repository as owner/name");
        RuleFor(x => x.Release!).SetValidator(new ProductReleaseValueObjectValidator()).When(x => x.Release is not null);
    }
}
