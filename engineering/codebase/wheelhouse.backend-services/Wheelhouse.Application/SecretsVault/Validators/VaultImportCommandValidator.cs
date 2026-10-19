using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.SecretsVault.Commands;

namespace Wheelhouse.Application.SecretsVault.Validators;

/// <summary>Validates an import: an inventory vault's slug.</summary>
public sealed class VaultImportCommandValidator : AbstractValidator<VaultImportCommand>
{
    /// <summary>Configures the rules.</summary>
    public VaultImportCommandValidator() => RuleFor(x => x.Vault).MustBeSlug();
}
