using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;

namespace Wheelhouse.Application.Inventory.Extensions;

/// <summary>Extends inventory validation with the rules every product, server, target and vault name shares.</summary>
public static class InventoryRuleExtensions
{
    /// <summary>Requires an inventory name: a lowercase letter, then lowercase letters, digits or dashes.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeSlug<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Matches(InventoryPatternConstants.Slug)
            .WithMessage("'{PropertyName}' must start with a lowercase letter and hold only lowercase letters, digits and dashes (48 at most).");

    /// <summary>Requires a display name of 1–80 characters.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeDisplayName<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Must(name => name is not null && name.Trim().Length is > 0 and <= 80)
            .WithMessage("'{PropertyName}' must hold 1–80 characters.");

    /// <summary>Requires text that matches one of the inventory patterns.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <param name="pattern">One of the <see cref="InventoryPatternConstants"/>.</param>
    /// <param name="shape">How the message names the expected shape.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustMatch<T>(this IRuleBuilder<T, string> rule, string pattern, string shape) =>
        rule
            .Must(value => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
            .WithMessage("'{PropertyName}' must be " + shape + ".");
}
