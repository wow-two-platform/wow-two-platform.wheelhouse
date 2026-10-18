using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the product automation entity and its product_automation table: one row per product, gone with it.</summary>
public sealed class ProductAutomationEntityConfiguration : IEntityTypeConfiguration<ProductAutomationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductAutomationEntity> builder)
    {
        builder
            .ToTable(ProductAutomationEntity.TableName);
        builder
            .HasKey(entity => entity.ProductId);
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
        builder
            .HasOne<ProductEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
