using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Settings.Entities;

/// <summary>Represents one non-secret setting value of a product environment, keyed by its configuration path, such as
/// <c>DataProtection:ApplicationName</c>. Each deploy renders it into the settings file of every service that declares
/// the key, or of every service that takes a settings file when none declares it. Secret values live in the vault.</summary>
public sealed class EnvironmentSettingEntity : IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "environment_settings";

    /// <summary>Gets or sets the product the setting belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the environment the value applies to.</summary>
    public DeploymentEnvironment Environment { get; set; }

    /// <summary>Gets or sets the configuration path, sections joined by <c>:</c>.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets the value, written into the settings file as a string.</summary>
    public required string Value { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
