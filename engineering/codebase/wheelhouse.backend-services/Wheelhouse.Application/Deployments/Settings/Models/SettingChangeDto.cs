namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents a key a deploy's render changed against the target's last good render; never a value.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Kind"><c>setting</c> or <c>secret</c>.</param>
/// <param name="Change"><c>added</c>, <c>changed</c> or <c>removed</c>.</param>
/// <param name="From">A secret's previous version.</param>
/// <param name="To">A secret's new version.</param>
public sealed record SettingChangeDto(string Key, string Kind, string Change, int? From = null, int? To = null);
