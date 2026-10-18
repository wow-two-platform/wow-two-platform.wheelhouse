using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents what the control plane deploys of one product on its own. A product without a row takes the
/// defaults: <see cref="AutoDeployDev"/> on, <see cref="AutoPromote"/> off.</summary>
public sealed class ProductAutomationEntity : IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "product_automation";

    /// <summary>Gets or sets the product; the row's key.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets whether each new published release deploys to the product's dev targets.</summary>
    public bool AutoDeployDev { get; set; } = true;

    /// <summary>Gets or sets whether a release that passed its dev watch window is promoted to prod.</summary>
    public bool AutoPromote { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
