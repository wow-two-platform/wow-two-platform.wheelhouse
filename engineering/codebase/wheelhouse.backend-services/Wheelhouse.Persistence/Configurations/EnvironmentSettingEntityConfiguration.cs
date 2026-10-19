using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Settings.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the environment setting entity and its environment_settings table: one row per product,
/// environment and key, gone with the product.</summary>
public sealed class EnvironmentSettingEntityConfiguration : IEntityTypeConfiguration<EnvironmentSettingEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnvironmentSettingEntity> builder)
    {
        builder
            .ToTable(EnvironmentSettingEntity.TableName);
        builder
            .HasKey(entity => new { entity.ProductId, entity.Environment, entity.Key });
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
