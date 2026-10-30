using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.SecretsVault.Validators;

/// <summary>Validates a history read: a target and a configuration path.</summary>
public sealed class SecretHistoryQueryValidator : AbstractValidator<SecretHistoryQuery>
{
    /// <summary>Configures the rules.</summary>
    public SecretHistoryQueryValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
    }
}
