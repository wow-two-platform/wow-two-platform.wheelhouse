using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Pins.Commands;
using Wheelhouse.Domain.Pins.Enums;

namespace Wheelhouse.Application.Pins.Validators;

/// <summary>Validates a pin request: it names exactly the target its kind takes.</summary>
public sealed class PinCreateCommandValidator : AbstractValidator<PinCreateCommand>
{
    /// <summary>Configures the pin rules.</summary>
    public PinCreateCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();

        RuleFor(x => x.WorkspaceId)
            .Must(id => id is { } workspace && workspace != Guid.Empty).When(x => x.Kind == PinKind.Workspace)
            .WithMessage("A workspace pin names its 'workspaceId'.");
        RuleFor(x => x.WorkspaceId)
            .Null().When(x => x.Kind != PinKind.Workspace)
            .WithMessage("Only a workspace pin names a 'workspaceId'.");

        RuleFor(x => x.Product!)
            .MustBeProductKey().When(x => x.Kind is PinKind.Product or PinKind.Service)
            .WithMessage("A product or service pin names its product's identifier or slug in 'productId'.");
        RuleFor(x => x.Product)
            .Null().When(x => x.Kind == PinKind.Workspace)
            .WithMessage("A workspace pin names no 'productId'.");

        RuleFor(x => x.Service!)
            .Must(service => service is not null && System.Text.RegularExpressions.Regex.IsMatch(service, InventoryPatternConstants.Slug))
            .When(x => x.Kind == PinKind.Service)
            .WithMessage("A service pin names the service's key in 'service'.");
        RuleFor(x => x.Service)
            .Null().When(x => x.Kind != PinKind.Service)
            .WithMessage("Only a service pin names a 'service'.");
    }
}
