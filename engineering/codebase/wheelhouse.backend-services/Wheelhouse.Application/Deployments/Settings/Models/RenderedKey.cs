namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents one key of a deploy's render, without its value.</summary>
/// <param name="Kind"><c>setting</c> or <c>secret</c>.</param>
/// <param name="Version">A secret's version; null for a setting or when the vault does not say.</param>
/// <param name="Fingerprint">A secret's keyed fingerprint, or a setting's digest.</param>
public sealed record RenderedKey(string Kind, int? Version, string? Fingerprint);
