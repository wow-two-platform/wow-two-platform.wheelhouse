using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a settings apply: a target, a time within 90 days, a one-line reason of 200 characters at most.</summary>
public sealed class SettingsApplyCommandValidator : AbstractValidator<SettingsApplyCommand>
{
    /// <summary>Configures the rules.</summary>
    public SettingsApplyCommandValidator(TimeProvider time)
    {
        RuleFor(x => x.Target).MustBeSlug();
        RuleFor(x => x.Target).Must((command, target) => command.Confirm is null || command.Confirm == target)
            .WithMessage("Type the target ID to confirm.");
        RuleFor(x => x.At).Must(at => at is null || at <= time.GetUtcNow().AddDays(90))
            .WithMessage("Schedule an apply within 90 days.");
        RuleFor(x => x.Reason!).Must(reason => reason.Length <= 200 && !reason.Any(char.IsControl)).When(x => x.Reason is not null)
            .WithMessage("A reason is one line of 200 characters at most.");
    }
}
