using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the deploy freeze entity and its deploy_freezes table: one freeze per scope, an environment only
/// with a product, gone with its product.</summary>
public sealed class DeployFreezeEntityConfiguration : IEntityTypeConfiguration<DeployFreezeEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DeployFreezeEntity> builder)
    {
        builder
            .ToTable(DeployFreezeEntity.TableName,
                table => table.HasCheckConstraint("ck_deploy_freezes_scope", "environment IS NULL OR product_id IS NOT NULL"));
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => new { entity.ProductId, entity.Environment })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ix_deploy_freezes_scope");
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .HasOne<ProductEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
