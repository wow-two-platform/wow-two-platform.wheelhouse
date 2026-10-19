namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents one service's settings file on a target: whether a host file is one of its layers, and each key.</summary>
/// <param name="Name">The service's name in the release.</param>
/// <param name="HostFile">Whether the target names a host settings file for it, rendered beneath Wheelhouse's values.</param>
/// <param name="Keys">Its settings, required ones first.</param>
public sealed record DeploySettingsServiceDto(string Name, bool HostFile, IReadOnlyList<DeploySettingDto> Keys);
