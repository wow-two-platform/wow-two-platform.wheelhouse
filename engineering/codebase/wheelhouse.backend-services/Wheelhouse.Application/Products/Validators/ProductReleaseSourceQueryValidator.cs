using FluentValidation;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a request for the release source a repository gives.</summary>
public sealed class ProductReleaseSourceQueryValidator : AbstractValidator<ProductReleaseSourceQuery>
{
    /// <summary>Configures the release source read rules.</summary>
    public ProductReleaseSourceQueryValidator()
    {
        RuleFor(x => x.Slug).MustBeNameSlug();
        RuleFor(x => x.Repository).MustMatch(FormatPatternConstants.GitHubRepository, "a GitHub repository as owner/name");
        RuleFor(x => x.Branch).MustMatch(FormatPatternConstants.GitBranch, "a branch name");
    }
}
