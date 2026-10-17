using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request to mark one of a product's services active or inactive.</summary>
public sealed class ServiceStatusUpdateCommandValidator : AbstractValidator<ServiceStatusUpdateCommand>
{
    /// <summary>Configures the service status-update rules.</summary>
    public ServiceStatusUpdateCommandValidator()
    {
        RuleFor(x => x.Product).MustBeProductKey();
        RuleFor(x => x.Service).MustBeSlug();
        RuleFor(x => x.Status).IsInEnum();
    }
}
