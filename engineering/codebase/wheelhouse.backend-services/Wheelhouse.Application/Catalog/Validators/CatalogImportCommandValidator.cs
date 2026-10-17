using FluentValidation;
using Wheelhouse.Application.Catalog.Commands;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Catalog.Validators;

/// <summary>Validates an import request: one to 200 entry slugs.</summary>
public sealed class CatalogImportCommandValidator : AbstractValidator<CatalogImportCommand>
{
    /// <summary>Configures the import rules.</summary>
    public CatalogImportCommandValidator()
    {
        RuleFor(x => x.Slugs)
            .NotEmpty()
            .Must(slugs => slugs.Count <= 200)
            .WithMessage("'{PropertyName}' must name 1–200 entries.");
        RuleForEach(x => x.Slugs).MustBeSlug();
    }
}
