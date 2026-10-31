using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a regeneration: a target, a configuration path, a note of one line, a release by its slug.</summary>
public sealed class SecretRegenerateCommandValidator : AbstractValidator<SecretRegenerateCommand>
{
    /// <summary>Configures the rules.</summary>
    public SecretRegenerateCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
        RuleFor(x => x.Note!).Must(note => note.Length <= 300 && !note.Any(char.IsControl)).When(x => x.Note is not null)
            .WithMessage("A note is one line of 300 characters at most.");
        RuleFor(x => x.Release!).MustBeNameSlug().When(x => x.Release is not null);
    }
}
