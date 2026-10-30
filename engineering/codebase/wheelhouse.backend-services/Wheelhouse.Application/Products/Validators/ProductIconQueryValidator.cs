using FluentValidation;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request for a product's or service's icon.</summary>
public sealed class ProductIconQueryValidator : AbstractValidator<ProductIconQuery>
{
    /// <summary>Configures the icon request rules.</summary>
    public ProductIconQueryValidator()
    {
        RuleFor(x => x.Slug).MustBeNameSlug();
        RuleFor(x => x.Icon.Service!).MustBeNameSlug().When(x => x.Icon.Service is not null);
        RuleFor(x => x.Icon.Theme).IsInEnum();
        RuleFor(x => x.Icon.Form).IsInEnum();
        RuleFor(x => x.Icon.Color).IsInEnum();
    }
}
