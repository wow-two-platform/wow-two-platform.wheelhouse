namespace Wheelhouse.Api.Requests;

/// <summary>Applies an environment's edited settings and secrets.</summary>
/// <param name="At">When to apply; null or a time within a minute applies now.</param>
/// <param name="Reason">The owner's one-line why, kept on the deploy.</param>
/// <param name="Confirm">The typed target ID, where the target asks for one.</param>
public sealed record SettingsApplyRequest(DateTimeOffset? At = null, string? Reason = null, string? Confirm = null);
