using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Queries;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a settings plan read: a target slug and, when given, a release bundle identifier.</summary>
public sealed class DeploymentSettingsQueryValidator : AbstractValidator<DeploymentSettingsQuery>
{
    /// <summary>Configures the rules.</summary>
    public DeploymentSettingsQueryValidator()
    {
        RuleFor(x => x.Target).MustBeSlug();
        RuleFor(x => x.Release!).MustBeSlug().When(x => x.Release is not null);
    }
}
