using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.SecretsVault.Commands;

namespace Wheelhouse.Application.SecretsVault.Validators;

/// <summary>Validates a restore: a target, a configuration path, a version from 1, a note of one line.</summary>
public sealed class SecretRestoreCommandValidator : AbstractValidator<SecretRestoreCommand>
{
    /// <summary>Configures the rules.</summary>
    public SecretRestoreCommandValidator()
    {
        RuleFor(x => x.Target).MustBeSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Note!).Must(note => note.Length <= 300 && !note.Any(char.IsControl)).When(x => x.Note is not null)
            .WithMessage("A note is one line of 300 characters at most.");
    }
}
