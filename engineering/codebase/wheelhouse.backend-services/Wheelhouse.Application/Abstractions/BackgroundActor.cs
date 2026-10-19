namespace Wheelhouse.Application.Abstractions;

/// <summary>Names whom work outside a request acts for in its scope, such as the owner whose fleet rollout a background
/// pass continues; unset, that work acts as <c>automation</c>. A request's own actor always wins.</summary>
public sealed class BackgroundActor
{
    /// <summary>Gets or sets the actor background work in this scope acts for; null acts as automation.</summary>
    public string? Actor { get; set; }
}
