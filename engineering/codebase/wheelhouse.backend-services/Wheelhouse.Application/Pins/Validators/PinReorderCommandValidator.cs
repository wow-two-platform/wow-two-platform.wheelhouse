using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Pins.Commands;

namespace Wheelhouse.Application.Pins.Validators;

/// <summary>Validates a request to reorder pins: a list naming each one once.</summary>
public sealed class PinReorderCommandValidator : AbstractValidator<PinReorderCommand>
{
    /// <summary>Configures the reorder rules.</summary>
    public PinReorderCommandValidator()
    {
        RuleFor(x => x.Ids).MustListEachOnce();
    }
}
