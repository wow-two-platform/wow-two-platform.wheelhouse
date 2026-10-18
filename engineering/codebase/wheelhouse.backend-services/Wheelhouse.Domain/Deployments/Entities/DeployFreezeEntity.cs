using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents a hold on deploys: everywhere (no product), one product (no environment) or one product's
/// environment. While a freeze covers a target, automatic deploys skip it and an owner's deploy needs an override with a
/// reason.</summary>
public sealed record DeployFreezeEntity : IKeyedEntity<Guid>, IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "deploy_freezes";

    /// <summary>Gets or sets the freeze's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the frozen product, or <c>null</c> for a freeze of every product.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the frozen environment of the product, or <c>null</c> for all of them.</summary>
    public DeploymentEnvironment? Environment { get; set; }

    /// <summary>Gets or sets why it was frozen, as the owner wrote it.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets who froze it.</summary>
    public required string Actor { get; set; }

    /// <summary>Gets or sets when it was frozen; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
