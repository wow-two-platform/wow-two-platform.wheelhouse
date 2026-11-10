using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Settings.Entities;

/// <summary>Represents an owner's link from a product environment's setting key to another product's key in the same
/// environment, such as a client secret one product's vault generates and another product presents: each deploy renders
/// the source's current value under the linking key, so the value reaches it unchanged and is never shown. One per
/// product, environment and key; links go one hop.</summary>
public sealed class SettingLinkEntity : IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "setting_links";

    /// <summary>Gets or sets the product whose key takes the source's value.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the environment the link applies to; the source's target is in the same one.</summary>
    public DeploymentEnvironment Environment { get; set; }

    /// <summary>Gets or sets the linking key's configuration path, sections joined by <c>:</c>.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets the target of the other product whose environment holds the source value.</summary>
    public Guid SourceTargetId { get; set; }

    /// <summary>Gets or sets the source key's configuration path in that environment.</summary>
    public required string SourceKey { get; set; }

    /// <summary>Gets or sets who linked it.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>Gets or sets when it was linked; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
