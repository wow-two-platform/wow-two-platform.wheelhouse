using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures an owner's link from a product environment's key to another product's key: one per product,
/// environment and key, gone with the product and with the source's target.</summary>
public sealed class SettingLinkEntityConfiguration : IEntityTypeConfiguration<SettingLinkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingLinkEntity> builder)
    {
        builder.ToTable(SettingLinkEntity.TableName);
        builder.HasKey(entity => new { entity.ProductId, entity.Environment, entity.Key });
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
        builder.HasOne<ProductEntity>().WithMany().HasForeignKey(entity => entity.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TargetEntity>().WithMany().HasForeignKey(entity => entity.SourceTargetId).OnDelete(DeleteBehavior.Cascade);
    }
}
