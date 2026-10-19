using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.SecretsVault.Queries;

namespace Wheelhouse.Application.SecretsVault.Validators;

/// <summary>Validates a history read: a target and a configuration path.</summary>
public sealed class SecretHistoryQueryValidator : AbstractValidator<SecretHistoryQuery>
{
    /// <summary>Configures the rules.</summary>
    public SecretHistoryQueryValidator()
    {
        RuleFor(x => x.Target).MustBeSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
    }
}
