using System.ComponentModel.DataAnnotations;
using Wheelhouse.Application.Deployments.Settings.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Gives a target's environment setting values; Wheelhouse stores settings and forwards secrets to the vault,
/// and never returns a secret.</summary>
/// <param name="Values">The values, each a configuration path, its value and whether it is a secret.</param>
public sealed record DeploymentSettingsSaveRequest([Required, MinLength(1), MaxLength(100)] IReadOnlyList<DeploymentSettingValueRequest> Values)
{
    /// <summary>Maps the request to the values the command takes.</summary>
    public IReadOnlyList<DeploySettingValue> ToValues() =>
        [.. Values.Select(value => new DeploySettingValue(value.Key, value.Value, value.Secret))];

    /// <summary>Returns the request without its values, so logs and exceptions cannot print one.</summary>
    public override string ToString() => nameof(DeploymentSettingsSaveRequest) + " { " + Values.Count + " values }";
}

/// <summary>One value of a <see cref="DeploymentSettingsSaveRequest"/>.</summary>
/// <param name="Key">The configuration path, such as <c>DataProtection:ApplicationName</c>.</param>
/// <param name="Value">The value.</param>
/// <param name="Secret">Whether it goes to the vault.</param>
public sealed record DeploymentSettingValueRequest(
    [Required, StringLength(1009)] string Key, [Required, StringLength(65536)] string Value, bool Secret = false)
{
    /// <summary>Returns the key and kind only.</summary>
    public override string ToString() => nameof(DeploymentSettingValueRequest) + " { " + Key + " }";
}
