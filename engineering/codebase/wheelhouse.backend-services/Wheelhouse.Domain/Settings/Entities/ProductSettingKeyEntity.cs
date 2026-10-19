using Wheelhouse.Domain.Settings.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Settings.Entities;

/// <summary>Represents a setting or secret key the owner added to a product in the console, beside the keys its
/// releases declare: its type and whether it is a secret. Its values are kept per environment like any key's.</summary>
public sealed class ProductSettingKeyEntity : IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "product_setting_keys";

    /// <summary>Gets or sets the product the key belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the configuration path, sections joined by <c>:</c>.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets whether its values belong in the vault.</summary>
    public bool Secret { get; set; }

    /// <summary>Gets or sets what its values must look like.</summary>
    public SettingType Type { get; set; }

    /// <summary>Gets or sets a date key's format, such as <c>yyyy-MM-dd</c>; null for other types.</summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets a choice key's values, one per line; null for other types.</summary>
    public string? Choices { get; set; }

    /// <summary>Gets or sets whether a deploy waits until the key has a value.</summary>
    public bool Required { get; set; }

    /// <summary>Gets or sets the owner's note on what the key is for.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets who added it.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>Gets or sets when it was added; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when it last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
