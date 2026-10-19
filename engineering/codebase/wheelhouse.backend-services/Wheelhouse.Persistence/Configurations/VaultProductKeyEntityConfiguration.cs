using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures a product's wrapped data key, gone with the product.</summary>
public sealed class VaultProductKeyEntityConfiguration : IEntityTypeConfiguration<VaultProductKeyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VaultProductKeyEntity> builder)
    {
        builder.ToTable(VaultProductKeyEntity.TableName, SecretsVaultSchema.Name);
        builder.HasKey(entity => entity.ProductId);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
        builder.HasOne<ProductEntity>().WithMany().HasForeignKey(entity => entity.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
