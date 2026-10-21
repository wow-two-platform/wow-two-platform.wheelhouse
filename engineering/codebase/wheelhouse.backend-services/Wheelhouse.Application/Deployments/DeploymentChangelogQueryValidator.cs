using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments;

/// <summary>Validates a changelog read: a product slug and release tags (<c>vX.Y.Z</c>, an optional pre-release), or the
/// labels of two release sets of a product built from several repositories.</summary>
public sealed class DeploymentChangelogQueryValidator : AbstractValidator<DeploymentChangelogQuery>
{
    /// <summary>Holds the release tag pattern the shared publish workflow tags with.</summary>
    public const string ReleaseTag = @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$";

    /// <summary>Holds the shape of a release set's label (decision 70): each repository's short name and release tag or
    /// commit build, joined by <c>_</c>, or <c>set-</c> and the set's 12-hex digest.</summary>
    public const string SetLabel =
        @"^(set-[0-9a-f]{12}|" + Part + "(_" + Part + ")*)$";

    /// <summary>Holds one repository's part of a release set's label.</summary>
    private const string Part =
        @"[a-z][a-z0-9-]*-(v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?|sha-[0-9a-f]{7})";

    /// <summary>Configures the rules.</summary>
    public DeploymentChangelogQueryValidator()
    {
        RuleFor(x => x.Product).MustBeSlug();
        RuleFor(x => x.To).NotEmpty().Must(IsRelease).WithMessage("'To' must be a release tag such as v1.2.0, or a release set.");
        RuleFor(x => x.From!).Must(IsRelease).When(x => x.From is not null)
            .WithMessage("'From' must be a release tag such as v1.2.0, or a release set.");
    }

    /// <summary>Whether a label is a release tag or a release set's label.</summary>
    private static bool IsRelease(string? label) =>
        label is { Length: <= 96 }
        && (System.Text.RegularExpressions.Regex.IsMatch(label, ReleaseTag) || System.Text.RegularExpressions.Regex.IsMatch(label, SetLabel));
}
