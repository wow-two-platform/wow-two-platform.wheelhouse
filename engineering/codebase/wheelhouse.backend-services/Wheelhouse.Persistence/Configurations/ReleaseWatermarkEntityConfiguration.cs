using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the release watermark entity and its release_watermarks table: one row per product, gone with it.</summary>
public sealed class ReleaseWatermarkEntityConfiguration : IEntityTypeConfiguration<ReleaseWatermarkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReleaseWatermarkEntity> builder)
    {
        builder
            .ToTable(ReleaseWatermarkEntity.TableName);
        builder
            .HasKey(entity => entity.ProductId);
        builder
            .Property(entity => entity.CheckedAt)
            .HasColumnName("checked_at_utc");
        builder
            .HasOne<ProductEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
