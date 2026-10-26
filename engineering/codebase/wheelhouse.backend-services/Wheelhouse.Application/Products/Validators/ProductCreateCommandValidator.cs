using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request to add a product.</summary>
public sealed class ProductCreateCommandValidator : AbstractValidator<ProductCreateCommand>
{
    /// <summary>Configures the product creation rules.</summary>
    public ProductCreateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x.Repository).MustMatch(InventoryPatternConstants.Repository, "a GitHub repository as owner/name");
        RuleFor(x => x.DefaultBranch).MustMatch(InventoryPatternConstants.Branch, "a branch name");
        RuleFor(x => x.Release!).SetValidator(new ProductReleaseRequestValidator()).When(x => x.Release is not null);
        RuleFor(x => x.Components!).Must(components => components.Count <= 10)
            .WithMessage("A product spans 10 repositories at most.").When(x => x.Components is not null);
        RuleForEach(x => x.Components).SetValidator(new ProductComponentValueObjectValidator());
        RuleFor(x => x.Deploy).IsInEnum();
        RuleFor(x => x.WorkspaceId).NotEqual(Guid.Empty);
    }
}
