using FluentValidation;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Products.Commands;

namespace Wheelhouse.Application.Products.Validators;

/// <summary>Validates a placement: databases, sites or both, each site once with its own node.</summary>
public sealed class ProductPlaceCommandValidator : AbstractValidator<ProductPlaceCommand>
{
    /// <summary>Configures the rules.</summary>
    public ProductPlaceCommandValidator()
    {
        RuleFor(x => x.Product).MustBeSlug();
        RuleFor(x => x.Server).MustBeSlug();
        RuleFor(x => x.Sites).NotNull().Must(sites => sites.Count <= 10).WithMessage("'Sites' may hold 10 sites at most.");
        RuleFor(x => x).Must(x => x.Database || x.Sites.Count > 0).OverridePropertyName(nameof(ProductPlaceCommand.Sites))
            .WithMessage("Choose databases, sites or both.");
        RuleForEach(x => x.Sites).ChildRules(site =>
        {
            site.RuleFor(x => x.Site).MustBeSlug();
            site.RuleFor(x => x.Node).Matches(InfraPatterns.Node)
                .WithMessage("'Node' must be a lowercase node name, such as ocharo-desk.")
                .Must(node => node is not "postgres" and not "ingress").WithMessage("'Node' names a platform service.");
        });
        RuleFor(x => x.Sites).Must(sites =>
            {
                var names = sites.SelectMany(site => new[] { site.Node, site.Node + "-dev" }).ToList();
                return names.Distinct(StringComparer.Ordinal).Count() == names.Count
                       && sites.Select(site => site.Site).Distinct(StringComparer.Ordinal).Count() == sites.Count;
            })
            .WithMessage("Each site and node may appear once.");
    }
}
