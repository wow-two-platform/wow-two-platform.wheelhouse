using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Json;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the product entity and its products table; a product holds its workspace in place.</summary>
public sealed class ProductEntityConfiguration : IEntityTypeConfiguration<ProductEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductEntity> builder)
    {
        builder
            .ToTable(ProductEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Slug)
            .IsUnique();
        builder
            .Property(entity => entity.Release)
            .HasColumnType("jsonb")
            .HasJsonConversion();
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
        builder
            .HasOne<WorkspaceEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
