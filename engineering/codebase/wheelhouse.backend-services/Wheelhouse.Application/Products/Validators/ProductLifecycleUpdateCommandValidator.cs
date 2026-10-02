using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request to record a catalog product's lifecycle.</summary>
public sealed class ProductLifecycleUpdateCommandValidator : AbstractValidator<ProductLifecycleUpdateCommand>
{
    /// <summary>Configures the lifecycle-update rules.</summary>
    public ProductLifecycleUpdateCommandValidator()
    {
        RuleFor(x => x.Slug)
            .MustBeSlug();

        RuleFor(x => x.Lifecycle)
            .IsInEnum();
    }
}
