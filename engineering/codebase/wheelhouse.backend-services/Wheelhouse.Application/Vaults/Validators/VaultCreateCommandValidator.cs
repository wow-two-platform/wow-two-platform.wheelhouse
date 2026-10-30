using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Vaults.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Vaults.Validators;

/// <summary>Validates a request to add a secrets vault Wheelhouse administers.</summary>
public sealed class VaultCreateCommandValidator : AbstractValidator<VaultCreateCommand>
{
    /// <summary>Configures the vault rules.</summary>
    public VaultCreateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeNameSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Server).MustBeNameSlug();
        RuleFor(x => x.Url).MustMatch(FormatPatternConstants.HttpEndpoint, "an address such as http://vault:8080");
    }
}
