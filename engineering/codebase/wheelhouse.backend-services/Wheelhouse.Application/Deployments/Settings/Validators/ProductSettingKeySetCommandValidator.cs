using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates an added key: a configuration path the render does not derive, a known type, a choice key's 1-50
/// values of one line each, a date key's format, a note of one line.</summary>
public sealed class ProductSettingKeySetCommandValidator : AbstractValidator<ProductSettingKeySetCommand>
{
    /// <summary>Configures the rules.</summary>
    public ProductSettingKeySetCommandValidator()
    {
        RuleFor(x => x.Product).MustBeSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
        RuleFor(x => x.Key).Must(key => !DeploymentSettingsService.IsDerived(key))
            .WithMessage(x => x.Key + " is derived at each deploy; it takes no value.");
        RuleFor(x => x.Type).Must(type => SettingTypes.Parse(type) is not null)
            .WithMessage("The type is one of " + string.Join(", ", SettingTypes.Names) + ".");
        RuleFor(x => x.Choices).NotEmpty().When(x => x.Type == "choice").WithMessage("A choice key lists its values.");
        RuleFor(x => x.Choices!).Must(choices => choices.Count <= SettingTypes.MaxChoices
                && choices.All(choice => choice.Trim().Length is > 0 and <= 200 && !choice.Any(char.IsControl)))
            .When(x => x.Choices is not null).WithMessage("A choice key lists 1-50 values of one line each.");
        RuleFor(x => x.Format).Must(SettingTypes.IsDateFormat).When(x => x.Type == "date" && x.Format is not null)
            .WithMessage("A date format uses yyyy, MM, dd, HH, mm and ss with - . / : T or spaces, such as yyyy-MM-dd.");
        RuleFor(x => x.Note!).Must(note => note.Length <= 300 && !note.Any(char.IsControl)).When(x => x.Note is not null)
            .WithMessage("A note is one line of 300 characters at most.");
    }
}
