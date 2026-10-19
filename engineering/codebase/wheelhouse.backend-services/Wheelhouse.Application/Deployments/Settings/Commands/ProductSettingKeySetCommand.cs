using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Adds a setting or secret key to a product in the console, beside the keys its releases declare, or changes
/// one the owner added.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Key">The configuration path.</param>
/// <param name="Secret">Whether its values go to the vault.</param>
/// <param name="Type">What its values must look like, by name.</param>
/// <param name="Format">A date key's format.</param>
/// <param name="Choices">A choice key's values.</param>
/// <param name="Required">Whether a deploy waits for a value.</param>
/// <param name="Note">What the key is for.</param>
/// <param name="ConfirmPlain">Whether the owner confirmed a key that reads as a secret is a plain setting.</param>
/// <param name="Actor">Who adds it.</param>
public sealed record ProductSettingKeySetCommand(
    string Product, string Key, bool Secret, string Type, string? Format, IReadOnlyList<string>? Choices, bool Required,
    string? Note, bool ConfirmPlain, string Actor) : ICommand<AppResult<DeploySettingDto>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "product.setting-key.set";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string AuditDetail => Key + " (" + Type + (Secret ? ", secret" : "") + (Required ? ", required" : "") + ")";
}
