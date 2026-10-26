using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Models;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates the values a release request sends; each one left out is derived from the repository.</summary>
public sealed class ProductReleaseRequestValidator : AbstractValidator<ProductReleaseRequest>
{
    /// <summary>Configures the release request rules.</summary>
    public ProductReleaseRequestValidator()
    {
        RuleFor(x => x.Asset!)
            .MustMatch(InventoryPatternConstants.Asset, "a release asset's file name")
            .When(x => !string.IsNullOrEmpty(x.Asset));

        RuleFor(x => x.Workflow!)
            .MustMatch(InventoryPatternConstants.Workflow, "a workflow file name ending in .yml or .yaml")
            .When(x => !string.IsNullOrEmpty(x.Workflow));

        RuleFor(x => x.Images!)
            .Must(images => images.Select(image => image.Service).Distinct(StringComparer.Ordinal).Count() == images.Count)
            .WithMessage("Each service publishes to one image repository.")
            .When(x => x.Images is not null);

        RuleForEach(x => x.Images)
            .ChildRules(image =>
            {
                image.RuleFor(x => x.Service).MustBeSlug();
                image.RuleFor(x => x.Image).MustMatch(InventoryPatternConstants.Image, "an image repository such as ghcr.io/owner/product/api");
            });
    }
}
