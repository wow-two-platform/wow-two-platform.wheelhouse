using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.SecretsVault.Validators;

/// <summary>Validates an expiry: a target and a configuration path.</summary>
public sealed class SecretExpiryCommandValidator : AbstractValidator<SecretExpiryCommand>
{
    /// <summary>Configures the rules.</summary>
    public SecretExpiryCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
    }
}
