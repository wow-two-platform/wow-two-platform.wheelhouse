using Wheelhouse.Application.Deployments.Settings.Models;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Holds what a settings save wrote, for its audit entry: key names and versions only.</summary>
public sealed class SettingsSaveRecord
{
    /// <summary>Gets or sets each key the save touched.</summary>
    public IReadOnlyList<SavedSettingDto> Keys { get; set; } = [];
}
