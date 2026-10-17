using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the service status entity and its service_statuses table: one row per product and service, gone
/// with the product.</summary>
public sealed class ServiceStatusEntityConfiguration : IEntityTypeConfiguration<ServiceStatusEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceStatusEntity> builder)
    {
        builder
            .ToTable(ServiceStatusEntity.TableName);
        builder
            .HasKey(entity => new { entity.ProductId, entity.Service });
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
