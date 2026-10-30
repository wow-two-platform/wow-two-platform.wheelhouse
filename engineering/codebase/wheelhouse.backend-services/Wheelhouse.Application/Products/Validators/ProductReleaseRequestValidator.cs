using FluentValidation;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates the values a release request sends; each one left out is derived from the repository.</summary>
public sealed class ProductReleaseRequestValidator : AbstractValidator<ProductReleaseRequest>
{
    /// <summary>Configures the release request rules.</summary>
    public ProductReleaseRequestValidator()
    {
        RuleFor(x => x.Asset!)
            .MustMatch(FormatPatternConstants.FileName, "a release asset's file name")
            .When(x => !string.IsNullOrEmpty(x.Asset));

        RuleFor(x => x.Workflow!)
            .MustMatch(FormatPatternConstants.WorkflowFileName, "a workflow file name ending in .yml or .yaml")
            .When(x => !string.IsNullOrEmpty(x.Workflow));

        RuleFor(x => x.Images!)
            .Must(images => images.Select(image => image.Service).Distinct(StringComparer.Ordinal).Count() == images.Count)
            .WithMessage("Each service publishes to one image repository.")
            .When(x => x.Images is not null);

        RuleForEach(x => x.Images)
            .ChildRules(image =>
            {
                image.RuleFor(x => x.Service).MustBeNameSlug();
                image.RuleFor(x => x.Image).MustMatch(FormatPatternConstants.ImageRepository, "an image repository such as ghcr.io/owner/product/api");
            });
    }
}
