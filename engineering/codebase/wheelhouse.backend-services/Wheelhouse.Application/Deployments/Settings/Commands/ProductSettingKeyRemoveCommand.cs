using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Removes a key the owner added to a product; values already given stay until removed themselves.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Key">The configuration path.</param>
public sealed record ProductSettingKeyRemoveCommand(string Product, string Key) : ICommand<AppResult<bool>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "product.setting-key.remove";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string AuditDetail => Key;
}
