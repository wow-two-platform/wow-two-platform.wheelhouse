using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request to change a product's definition.</summary>
public sealed class ProductUpdateCommandValidator : AbstractValidator<ProductUpdateCommand>
{
    /// <summary>Configures the product update rules.</summary>
    public ProductUpdateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x.Repository).MustMatch(InventoryPatternConstants.Repository, "a GitHub repository as owner/name");
        RuleFor(x => x.DefaultBranch).MustMatch(InventoryPatternConstants.Branch, "a branch name");
        RuleFor(x => x.Release!).SetValidator(new ProductReleaseValueObjectValidator()).When(x => x.Release is not null);
    }
}
