using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Products.Entities;

/// <summary>Represents the status the operator gave one of a product's services, keyed by the product and the service's
/// key in its <c>deploy.yml</c>. A service without a row is active. One status covers every environment.</summary>
public sealed class ServiceStatusEntity : IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "service_statuses";

    /// <summary>Gets or sets the product the service belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the service's key under <c>services:</c> in the product's <c>deploy.yml</c>.</summary>
    public required string Service { get; set; }

    /// <summary>Gets or sets whether the service is in use.</summary>
    public ProductStatus Status { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
