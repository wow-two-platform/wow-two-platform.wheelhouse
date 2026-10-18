using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments;

/// <summary>Validates a changelog read: a product slug and release tags (<c>vX.Y.Z</c>, an optional pre-release).</summary>
public sealed class DeploymentChangelogQueryValidator : AbstractValidator<DeploymentChangelogQuery>
{
    /// <summary>Holds the release tag pattern the shared publish workflow tags with.</summary>
    public const string ReleaseTag = @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$";

    /// <summary>Configures the rules.</summary>
    public DeploymentChangelogQueryValidator()
    {
        RuleFor(x => x.Product).MustBeSlug();
        RuleFor(x => x.To).NotEmpty().Matches(ReleaseTag).WithMessage("'To' must be a release tag such as v1.2.0.");
        RuleFor(x => x.From!).Matches(ReleaseTag).When(x => x.From is not null)
            .WithMessage("'From' must be a release tag such as v1.2.0.");
    }
}
