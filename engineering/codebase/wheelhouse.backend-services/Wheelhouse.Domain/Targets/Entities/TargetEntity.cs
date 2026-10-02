using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Domain.Targets.Entities;

/// <summary>Represents one environment of a product on one server: where it runs, which settings files its services
/// read, which requests prove a rollout and which hosts its sites answer on.</summary>
public sealed record TargetEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "targets";

    /// <summary>Gets or sets the target's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name deployments and the runner know the target by, such as
    /// <c>foreverpin-prod</c>; fixed once the target exists.</summary>
    public required string Slug { get; set; }

    /// <summary>Gets or sets the product the target runs.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the server the target runs on.</summary>
    public Guid ServerId { get; set; }

    /// <summary>Gets or sets which stage of the product the target runs.</summary>
    public DeploymentEnvironment Environment { get; set; }

    /// <summary>Gets or sets the Docker network the target's services share with the host's platform services.</summary>
    public required string Network { get; set; }

    /// <summary>Gets or sets the folder on the host the runner keeps the target's releases under.</summary>
    public required string Root { get; set; }

    /// <summary>Gets or sets each service's settings file on the host.</summary>
    public IReadOnlyList<TargetSettingValueObject> Settings { get; set; } = [];

    /// <summary>Gets or sets the requests that prove a rollout.</summary>
    public IReadOnlyList<SmokeCheckValueObject> SmokeChecks { get; set; } = [];

    /// <summary>Gets or sets the hosts of sites the server's pattern does not cover; prod on a VPS names all of
    /// them.</summary>
    public IReadOnlyList<SiteHostValueObject> Sites { get; set; } = [];

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
