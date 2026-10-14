using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Queries;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request for a product's or service's icon.</summary>
public sealed class ProductIconQueryValidator : AbstractValidator<ProductIconQuery>
{
    /// <summary>Configures the icon request rules.</summary>
    public ProductIconQueryValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Icon.Service!).MustBeSlug().When(x => x.Icon.Service is not null);
        RuleFor(x => x.Icon.Theme).IsInEnum();
        RuleFor(x => x.Icon.Form).IsInEnum();
        RuleFor(x => x.Icon.Color).IsInEnum();
    }
}
