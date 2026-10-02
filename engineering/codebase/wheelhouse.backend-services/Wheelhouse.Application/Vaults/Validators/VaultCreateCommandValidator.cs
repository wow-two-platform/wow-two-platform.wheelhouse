using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Vaults.Commands;

namespace Wheelhouse.Application.Vaults.Validators;

/// <summary>Validates a request to add a secrets vault Wheelhouse administers.</summary>
public sealed class VaultCreateCommandValidator : AbstractValidator<VaultCreateCommand>
{
    /// <summary>Configures the vault rules.</summary>
    public VaultCreateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Server).MustBeSlug();
        RuleFor(x => x.Url).MustMatch(InventoryPatternConstants.Endpoint, "an address such as http://vault:8080");
    }
}
