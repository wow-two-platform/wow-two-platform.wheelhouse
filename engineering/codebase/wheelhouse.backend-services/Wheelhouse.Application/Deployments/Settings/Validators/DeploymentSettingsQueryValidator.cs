using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a settings plan read: a target slug and, when given, a release bundle identifier.</summary>
public sealed class DeploymentSettingsQueryValidator : AbstractValidator<DeploymentSettingsQuery>
{
    /// <summary>Configures the rules.</summary>
    public DeploymentSettingsQueryValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Release!).MustBeNameSlug().When(x => x.Release is not null);
    }
}
