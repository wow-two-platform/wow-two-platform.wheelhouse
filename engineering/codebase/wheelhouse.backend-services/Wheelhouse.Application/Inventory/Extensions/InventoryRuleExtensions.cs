using FluentValidation;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Inventory.Extensions;

/// <summary>Extends inventory validation with the rules the SDK validation vocabulary does not carry; names, addresses and
/// shapes take the be sdk <c>FormatRuleExtensions</c>.</summary>
public static class InventoryRuleExtensions
{
    /// <summary>Requires the name a route gives a product: its identifier or its slug.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeProductKey<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Must(key => key is not null
                && (Guid.TryParse(key, out _) || System.Text.RegularExpressions.Regex.IsMatch(key, FormatPatternConstants.NameSlug)))
            .WithMessage("'{PropertyName}' must be a product's identifier or its slug.");

    /// <summary>Requires a display name of 1–80 characters.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeDisplayName<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Must(name => name is not null && name.Trim().Length is > 0 and <= 80)
            .WithMessage("'{PropertyName}' must hold 1–80 characters.");

    /// <summary>Requires an order: a list of identifiers, none empty or repeated, 500 at most.</summary>
    /// <param name="rule">The rule builder.</param>
    /// <typeparam name="T">The validated type.</typeparam>
    /// <returns>The rule builder for chaining.</returns>
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> MustListEachOnce<T>(this IRuleBuilder<T, IReadOnlyList<Guid>> rule) =>
        rule
            .Must(ids => ids is not null && ids.Count <= 500 && !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Count)
            .WithMessage("'{PropertyName}' must list each identifier once (500 at most).");
}
