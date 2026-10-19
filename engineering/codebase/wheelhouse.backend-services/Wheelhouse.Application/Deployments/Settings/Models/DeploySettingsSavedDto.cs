namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents what a save wrote: how many settings went to the store and how many secrets to the vault, and
/// each key with its new version.</summary>
/// <param name="Settings">The settings stored.</param>
/// <param name="Secrets">The secrets written to the vault.</param>
public sealed record DeploySettingsSavedDto(int Settings, int Secrets)
{
    /// <summary>Gets each key the save touched.</summary>
    public IReadOnlyList<SavedSettingDto> Keys { get; init; } = [];
}
