using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Queries;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request for the release source a repository gives.</summary>
public sealed class ProductReleaseSourceQueryValidator : AbstractValidator<ProductReleaseSourceQuery>
{
    /// <summary>Configures the release source read rules.</summary>
    public ProductReleaseSourceQueryValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Repository).MustMatch(InventoryPatternConstants.Repository, "a GitHub repository as owner/name");
        RuleFor(x => x.Branch).MustMatch(InventoryPatternConstants.Branch, "a branch name");
    }
}
