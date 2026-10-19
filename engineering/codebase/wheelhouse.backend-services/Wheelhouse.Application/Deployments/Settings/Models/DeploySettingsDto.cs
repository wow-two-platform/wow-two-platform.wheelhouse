namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents the settings a release takes on a target and where the next deploy's render takes each from.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Product">The product's slug.</param>
/// <param name="Environment">The environment, <c>dev</c> or <c>prod</c>.</param>
/// <param name="Release">The release's label.</param>
/// <param name="FirstDeploy">Whether no deploy succeeded on the target yet: the dialog then offers optional keys too.</param>
/// <param name="Vault">Where secret values live; null when the inventory has no vault.</param>
/// <param name="Services">Each service that takes a settings file.</param>
/// <param name="Missing">How many required keys no layer sets on services without a host file: each blocks a deploy.</param>
public sealed record DeploySettingsDto(
    string Target, string Product, string Environment, string Release, bool FirstDeploy, DeploySettingsVaultDto? Vault,
    IReadOnlyList<DeploySettingsServiceDto> Services, int Missing);
