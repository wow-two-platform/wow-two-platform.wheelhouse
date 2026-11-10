using FluentValidation;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Deployments.Freezes.Validators;
using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Controls.Validators;

/// <summary>Validates a control: a target, a known operation, a service key when one is named — a restart and a run
/// always name one, a reset never — a migration set's name only on a reset, a command only on a run, and a one-line reason
/// of 300 characters at most. No message ever quotes a command's value.</summary>
public sealed class TargetControlCommandValidator : AbstractValidator<TargetControlCommand>
{
    /// <summary>A migration set's name, as an image's <c>migrate sets --json</c> gives it and the runner accepts it.</summary>
    public const string SetName = "^[a-z][a-z0-9_-]{0,63}$";

    /// <summary>Holds how many arguments a run's command takes at most, as the runner does.</summary>
    public const int MaxCommandArguments = 40;

    /// <summary>Holds how many bytes a run's stdin file holds at most, as the runner does.</summary>
    public const int MaxInputBytes = 2 * 1024 * 1024;

    /// <summary>Holds how long one argument of a run's command may be, as the runner does.</summary>
    public const int MaxArgumentLength = 500;

    /// <summary>Configures the rules.</summary>
    public TargetControlCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Operation).IsInEnum();
        RuleFor(x => x.Service!).MustBeNameSlug().When(x => x.Service is not null);
        RuleFor(x => x.Service).NotNull().When(x => x.Operation == ControlOperation.Restart)
            .WithMessage("Restart one service at a time.");
        RuleFor(x => x.Service).NotNull().When(x => x.Operation == ControlOperation.Run)
            .WithMessage("Run a command in one service.");
        RuleFor(x => x.Service).Null().When(x => x.Operation == ControlOperation.Reset)
            .WithMessage("A reset takes the environment's database, never one service.");
        RuleFor(x => x.Command).Null().When(x => x.Operation != ControlOperation.Run)
            .WithMessage("Only a run takes a command.");
        RuleFor(x => x.Command).Must(IsCommand).When(x => x.Operation == ControlOperation.Run)
            .WithMessage($"A run takes its command as 1 to {MaxCommandArguments} arguments, each one line of at most "
                + $"{MaxArgumentLength} characters.");
        RuleFor(x => x.Input).Null().When(x => x.Operation != ControlOperation.Run)
            .WithMessage("Only a run takes a file on its stdin.");
        RuleFor(x => x.Input!).Must(IsInput).When(x => x.Input is not null)
            .WithMessage("A run's stdin file is base64 of 1 byte to 2 MiB.");
        RuleFor(x => x.Set).Null().When(x => x.Operation != ControlOperation.Reset)
            .WithMessage("Only a reset names a migration set.");
        RuleFor(x => x.Set!).Matches(SetName).When(x => x.Set is not null)
            .WithMessage("'Set' must be a migration set's name: lowercase letters, digits, '-' and '_'.");
        RuleFor(x => x.Reason).Must(FreezeRules.IsReason).WithMessage("'Reason' must be one line of 300 characters at most.");
    }

    /// <summary>Whether <paramref name="input"/> is base64 of 1 byte to <see cref="MaxInputBytes"/>.</summary>
    private static bool IsInput(string input)
    {
        if (input.Length == 0 || input.Length > (MaxInputBytes + 2) / 3 * 4)
            return false;
        var buffer = new byte[input.Length / 4 * 3 + 3];
        return Convert.TryFromBase64String(input, buffer, out var written) && written is > 0 and <= MaxInputBytes;
    }

    /// <summary>Whether <paramref name="command"/> is a run's command: 1 to 40 arguments, each one line of 1 to 500
    /// characters.</summary>
    private static bool IsCommand(IReadOnlyList<string>? command) =>
        command is { Count: > 0 and <= MaxCommandArguments }
        && command.All(argument => argument is { Length: > 0 and <= MaxArgumentLength }
            && argument.IndexOfAny(['\0', '\r', '\n']) < 0);
}
