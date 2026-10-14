using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Json;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the cached product manifest and its product_manifests table: one row per product, dropped with it.</summary>
public sealed class ProductManifestEntityConfiguration : IEntityTypeConfiguration<ProductManifestEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductManifestEntity> builder)
    {
        builder
            .ToTable(ProductManifestEntity.TableName);
        builder
            .HasKey(entity => entity.ProductId);
        builder
            .Property(entity => entity.Commit)
            .HasColumnName("commit_sha");
        builder
            .Property(entity => entity.Services)
            .HasColumnType("jsonb")
            .HasJsonConversion();
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
        builder
            .HasOne<ProductEntity>()
            .WithOne()
            .HasForeignKey<ProductManifestEntity>(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
