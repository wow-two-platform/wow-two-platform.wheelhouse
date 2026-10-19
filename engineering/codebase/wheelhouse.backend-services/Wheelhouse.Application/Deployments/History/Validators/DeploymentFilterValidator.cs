using FluentValidation;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates a deployment filter: inventory slugs, <c>product/service</c> pairs, a known time zone and an ordered
/// range.</summary>
public sealed class DeploymentFilterValidator : AbstractValidator<DeploymentFilter>
{
    /// <summary>Holds how many entities of one kind a filter may name.</summary>
    public const int MaxEntities = 100;

    /// <summary>Configures the filter rules.</summary>
    public DeploymentFilterValidator()
    {
        RuleFor(x => x.Workspaces).Must(items => items.Count <= MaxEntities)
            .WithMessage($"'workspace' may name {MaxEntities} workspaces at most.");
        RuleForEach(x => x.Workspaces).MustBeSlug().OverridePropertyName("workspace");
        RuleFor(x => x.Products).Must(items => items.Count <= MaxEntities)
            .WithMessage($"'product' may name {MaxEntities} products at most.");
        RuleForEach(x => x.Products).MustBeSlug().OverridePropertyName("product");
        RuleFor(x => x.Services).Must(items => items.Count <= MaxEntities)
            .WithMessage($"'service' may name {MaxEntities} services at most.");
        RuleForEach(x => x.Services)
            .Must(BeProductService)
            .WithMessage("'service' must name a product and one of its services, as product/service.");
        RuleFor(x => x.Status).IsInEnum()
            .WithMessage("'status' must be all, needs-action, look, succeeded or not-applied.");
        RuleFor(x => x.TimeZone)
            .Must(zone => zone is { Length: > 0 and <= 64 } && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            .WithMessage("'tz' must be an IANA time zone, such as Asia/Tashkent.");
        RuleFor(x => x.To)
            .Must((filter, to) => filter.From is not { } from || to is not { } end || from < end)
            .WithMessage("'from' must come before 'to'.");
    }

    /// <summary>Whether a value is two inventory slugs joined by a slash. @internal</summary>
    private static bool BeProductService(string value)
    {
        var parts = value?.Split('/') ?? [];
        return parts.Length == 2 && parts.All(part =>
            System.Text.RegularExpressions.Regex.IsMatch(part, Inventory.Constants.InventoryPatternConstants.Slug));
    }
}
