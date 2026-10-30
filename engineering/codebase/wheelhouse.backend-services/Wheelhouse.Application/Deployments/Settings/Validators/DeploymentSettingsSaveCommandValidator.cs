using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates setting values: configuration paths the runner accepts, each once, none the render derives; a
/// setting is one line of up to 2,000 characters, a secret up to 64 KiB; a note one line of 300 at most; a removal
/// carries no value, nor does a secret Wheelhouse generates (hex or base64url).</summary>
public sealed class DeploymentSettingsSaveCommandValidator : AbstractValidator<DeploymentSettingsSaveCommand>
{
    /// <summary>Configures the rules.</summary>
    public DeploymentSettingsSaveCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Release!).MustBeNameSlug().When(x => x.Release is not null);
        RuleFor(x => x.Values).NotEmpty().Must(values => values.Count <= 100)
            .WithMessage("Give between 1 and 100 values at once.");
        RuleFor(x => x.Values).Must(values => values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count() == values.Count)
            .WithMessage("Give each key once.");
        RuleForEach(x => x.Values).ChildRules(value =>
        {
            value.RuleFor(item => item.Key).Must(DeploymentSettingsService.IsKey)
                .WithMessage("A key is a configuration path such as Section:Key.");
            value.RuleFor(item => item.Key).Must(key => !DeploymentSettingsService.IsDerived(key))
                .WithMessage(item => item.Key + " is derived at each deploy; it takes no value.");
            value.RuleFor(item => item.Value).NotEmpty().When(item => !item.Remove && item.Generate is null)
                .WithMessage(item => item.Key + " needs a value.");
            value.RuleFor(item => item.Value).Empty().When(item => item.Generate is not null)
                .WithMessage(item => item.Key + " is generated; give no value.");
            value.RuleFor(item => item.Generate).Must(SecretValues.IsFormat).When(item => item.Generate is not null)
                .WithMessage(item => item.Key + ": a generated value is " + string.Join(" or ", SecretValues.Formats) + ".");
            value.RuleFor(item => item.Secret).Equal(true).When(item => item.Generate is not null)
                .WithMessage(item => item.Key + ": only a secret's value is generated.");
            value.RuleFor(item => item.Remove).Equal(false).When(item => item.Generate is not null)
                .WithMessage(item => item.Key + " is removed; nothing is generated.");
            value.RuleFor(item => item.Value).Empty().When(item => item.Remove).WithMessage(item => item.Key + " is removed; give no value.");
            value.RuleFor(item => item.Value).Must(text => text.Length <= 2000 && !text.Any(char.IsControl)).When(item => !item.Secret)
                .WithMessage(item => item.Key + " must be one line of 2,000 characters at most.");
            value.RuleFor(item => item.Value).Must(text => text.Length <= 65536).When(item => item.Secret)
                .WithMessage(item => item.Key + " must be 64 KiB at most.");
            value.RuleFor(item => item.Note!).Must(note => note.Length <= 300 && !note.Any(char.IsControl)).When(item => item.Note is not null)
                .WithMessage(item => item.Key + "'s note must be one line of 300 characters at most.");
            value.RuleFor(item => item.Note).Null().When(item => !item.Secret)
                .WithMessage(item => item.Key + ": a note goes with a secret's version.");
            value.RuleFor(item => item.Expiry).Null().When(item => !item.Secret || item.Remove)
                .WithMessage(item => item.Key + ": an expiry goes with a secret.");
        });
    }
}
