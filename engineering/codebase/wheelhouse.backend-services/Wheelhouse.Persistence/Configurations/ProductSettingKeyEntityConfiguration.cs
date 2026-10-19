using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the keys an owner added to a product: one per product and key, gone with the product.</summary>
public sealed class ProductSettingKeyEntityConfiguration : IEntityTypeConfiguration<ProductSettingKeyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductSettingKeyEntity> builder)
    {
        builder.ToTable(ProductSettingKeyEntity.TableName);
        builder.HasKey(entity => new { entity.ProductId, entity.Key });
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at_utc");
        builder.HasOne<ProductEntity>().WithMany().HasForeignKey(entity => entity.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
