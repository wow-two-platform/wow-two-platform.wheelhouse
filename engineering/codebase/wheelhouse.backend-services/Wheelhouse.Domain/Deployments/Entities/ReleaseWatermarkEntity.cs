using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents the newest published release the release watcher has seen of one product; a release published
/// after it is new. The watcher's first read of a product only records it.</summary>
public sealed class ReleaseWatermarkEntity : IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "release_watermarks";

    /// <summary>Gets or sets the product; the row's key.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the newest release label seen, or <c>null</c> while the product has published none.</summary>
    public string? Release { get; set; }

    /// <summary>Gets or sets when that release was published.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Gets or sets when the watcher last read the product's releases.</summary>
    public DateTimeOffset CheckedAt { get; set; }
}
