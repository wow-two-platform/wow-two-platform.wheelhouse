using FluentValidation;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a product's release source.</summary>
public sealed class ProductReleaseValueObjectValidator : AbstractValidator<ProductReleaseValueObject>
{
    /// <summary>Configures the release source rules.</summary>
    public ProductReleaseValueObjectValidator()
    {
        RuleFor(x => x.Asset)
            .MustMatch(FormatPatternConstants.FileName, "a release asset's file name");

        RuleFor(x => x.Workflow!)
            .MustMatch(FormatPatternConstants.WorkflowFileName, "a workflow file name ending in .yml or .yaml")
            .When(x => x.Workflow is not null);

        RuleFor(x => x.Images)
            .NotNull()
            .Must(images => images.Select(image => image.Service).Distinct(StringComparer.Ordinal).Count() == images.Count)
            .WithMessage("Each service publishes to one image repository.");

        RuleForEach(x => x.Images)
            .ChildRules(image =>
            {
                image.RuleFor(x => x.Service).MustBeNameSlug();
                image.RuleFor(x => x.Image).MustMatch(FormatPatternConstants.ImageRepository, "an image repository such as ghcr.io/owner/product/api");
            });
    }
}
