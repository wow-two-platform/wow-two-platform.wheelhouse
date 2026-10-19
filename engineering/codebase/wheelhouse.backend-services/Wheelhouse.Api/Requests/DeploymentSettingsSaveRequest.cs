using System.ComponentModel.DataAnnotations;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.SecretsVault.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Gives a target's environment setting values; Wheelhouse stores settings and keeps secrets in the vault as new
/// versions, in one transaction, and never returns a secret. One value saves one field; several land together.</summary>
/// <param name="Values">The values, each a configuration path, its value and whether it is a secret.</param>
/// <param name="Release">The release whose declarations type the values (the deploy dialog's); null for the one the
/// target runs.</param>
public sealed record DeploymentSettingsSaveRequest(
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<DeploymentSettingValueRequest> Values,
    [RegularExpression(Wheelhouse.Application.Inventory.Constants.InventoryPatternConstants.Slug)] string? Release = null)
{
    /// <summary>Maps the request to the values the command takes.</summary>
    public IReadOnlyList<DeploySettingValue> ToValues() =>
        [.. Values.Select(value => new DeploySettingValue(value.Key, value.Value ?? "", value.Secret)
        {
            Note = string.IsNullOrWhiteSpace(value.Note) ? null : value.Note,
            Expiry = value.Expiry is { } expiry ? new ExpiryChange(expiry.At) : null,
            ConfirmPlain = value.ConfirmPlain,
            Remove = value.Remove,
        })];

    /// <summary>Returns the request without its values, so logs and exceptions cannot print one.</summary>
    public override string ToString() => nameof(DeploymentSettingsSaveRequest) + " { " + Values.Count + " values }";
}

/// <summary>One value of a <see cref="DeploymentSettingsSaveRequest"/>.</summary>
/// <param name="Key">The configuration path, such as <c>DataProtection:ApplicationName</c>.</param>
/// <param name="Value">The value; empty with <paramref name="Remove"/>.</param>
/// <param name="Secret">Whether it goes to the vault.</param>
/// <param name="Note">A secret's note on the change, kept with its version.</param>
/// <param name="Expiry">A secret's expiry to set: <c>{"at": null}</c> clears it; absent keeps it.</param>
/// <param name="ConfirmPlain">Whether a key that reads as a secret is a plain setting all the same.</param>
/// <param name="Remove">Whether to remove the key's value from the store and the vault alike.</param>
public sealed record DeploymentSettingValueRequest(
    [Required, StringLength(1009)] string Key,
    [StringLength(65536)] string? Value,
    bool Secret = false,
    [StringLength(300)] string? Note = null,
    SecretExpiryValue? Expiry = null,
    bool ConfirmPlain = false,
    bool Remove = false)
{
    /// <summary>Returns the key and kind only.</summary>
    public override string ToString() => nameof(DeploymentSettingValueRequest) + " { " + Key + " }";
}

/// <summary>A secret's expiry as a save gives it.</summary>
/// <param name="At">When it lapses; null clears the expiry.</param>
public sealed record SecretExpiryValue(DateTimeOffset? At);
