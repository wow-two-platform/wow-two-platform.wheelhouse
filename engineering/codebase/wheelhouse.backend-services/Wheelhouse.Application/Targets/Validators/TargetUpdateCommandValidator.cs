using FluentValidation;
using Wheelhouse.Application.Targets.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Targets.Validators;

/// <summary>Validates a request to change a target's definition; its slug stays.</summary>
public sealed class TargetUpdateCommandValidator : AbstractValidator<TargetUpdateCommand>
{
    /// <summary>Configures the target rules.</summary>
    public TargetUpdateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeNameSlug();
        RuleFor(x => x.Product).MustBeNameSlug();
        RuleFor(x => x.Server).MustBeNameSlug();
        RuleFor(x => x.Environment).IsInEnum();
        RuleFor(x => x.Network).MustMatch(FormatPatternConstants.DockerNetwork, "a Docker network name");
        RuleFor(x => x.Root)
            .Must(root => root is not null && root.StartsWith('/') && root.TrimEnd('/') is not ("" or "/tmp" or "/srv")
                && !root.Contains("..", StringComparison.Ordinal) && root.Length <= 200)
            .WithMessage("'{PropertyName}' must be a dedicated absolute folder, such as /srv/wheelhouse.");
        RuleFor(x => x.Settings)
            .NotNull()
            .Must(settings => settings.Select(setting => setting.Service).Distinct(StringComparer.Ordinal).Count() == settings.Count)
            .WithMessage("Each service reads one settings file.");
        RuleForEach(x => x.Settings).ChildRules(setting =>
        {
            setting.RuleFor(x => x.Service).MustBeNameSlug();
            setting.RuleFor(x => x.Path)
                .Must(path => path is not null && path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal) && path.Length <= 300)
                .WithMessage("'{PropertyName}' must be an absolute path on the host.");
        });
        RuleFor(x => x.SmokeChecks).NotNull();
        RuleForEach(x => x.SmokeChecks).ChildRules(check =>
        {
            check.RuleFor(x => x.Service).MustBeNameSlug();
            check.RuleFor(x => x.Path).MustMatch(FormatPatternConstants.RequestPath, "a path starting with /");
            check.RuleFor(x => x.Status).InclusiveBetween(200, 499);
        });
        RuleFor(x => x.Sites)
            .NotNull()
            .Must(sites => sites.Select(site => site.Site).Distinct(StringComparer.Ordinal).Count() == sites.Count)
            .WithMessage("Each site answers on one host.");
        RuleForEach(x => x.Sites).ChildRules(site =>
        {
            site.RuleFor(x => x.Site).MustBeNameSlug();
            site.RuleFor(x => x.Host).MustMatch(FormatPatternConstants.DomainName, "a lowercase host name such as app.example.com");
        });
    }
}
