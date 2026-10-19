namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents the settings a release takes on a target and where the next deploy's render takes each from.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Product">The product's slug.</param>
/// <param name="Environment">The environment, <c>dev</c> or <c>prod</c>.</param>
/// <param name="Release">The release's label; null when the target runs none and none was named.</param>
/// <param name="FirstDeploy">Whether no deploy succeeded on the target yet: the dialog then offers optional keys too.</param>
/// <param name="Vault">Where secret values live; null when nothing can hold them.</param>
/// <param name="Services">Each service that takes a settings file.</param>
/// <param name="Missing">How many required keys no layer sets on services without a host file: each blocks a deploy.</param>
public sealed record DeploySettingsDto(
    string Target, string Product, string Environment, string? Release, bool FirstDeploy, DeploySettingsVaultDto? Vault,
    IReadOnlyList<DeploySettingsServiceDto> Services, int Missing)
{
    /// <summary>Gets the release bundle the plan read; null when there was none to read.</summary>
    public string? Bundle { get; init; }

    /// <summary>Gets the keys no service declares: those the owner added, and values stored or kept in the vault for
    /// keys no release names. Each goes to every service that takes a settings file.</summary>
    public IReadOnlyList<DeploySettingDto> Extra { get; init; } = [];

    /// <summary>Gets the keys whose values changed since the target's last deploy rendered them: an apply restarts the
    /// services they reach. Empty before a first deploy.</summary>
    public IReadOnlyList<string> Unapplied { get; init; } = [];

    /// <summary>Gets the target's scheduled apply, if one waits.</summary>
    public SettingsApplyDto? Pending { get; init; }
}
