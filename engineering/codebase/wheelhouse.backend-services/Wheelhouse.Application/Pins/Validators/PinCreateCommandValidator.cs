using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Pins.Commands;
using Wheelhouse.Domain.Pins.Enums;

namespace Wheelhouse.Application.Pins.Validators;

/// <summary>Validates a pin request: pins are products only (since 2026-10-18), named by identifier or slug.</summary>
public sealed class PinCreateCommandValidator : AbstractValidator<PinCreateCommand>
{
    /// <summary>Configures the pin rules.</summary>
    public PinCreateCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Kind)
            .Equal(PinKind.Product).When(x => Enum.IsDefined(x.Kind))
            .WithMessage("Only products are pinned; pin the product the workspace or service belongs to.");

        RuleFor(x => x.Product!)
            .MustBeProductKey()
            .WithMessage("A pin names its product's identifier or slug in 'productId'.");
        RuleFor(x => x.WorkspaceId)
            .Null()
            .WithMessage("A pin names no 'workspaceId'.");
        RuleFor(x => x.Service)
            .Null()
            .WithMessage("A pin names no 'service'.");
    }
}
