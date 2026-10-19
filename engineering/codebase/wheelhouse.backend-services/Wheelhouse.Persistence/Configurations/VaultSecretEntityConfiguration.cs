using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures a secret's metadata row: one per product, environment and key, gone with the product.</summary>
public sealed class VaultSecretEntityConfiguration : IEntityTypeConfiguration<VaultSecretEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VaultSecretEntity> builder)
    {
        builder.ToTable(VaultSecretEntity.TableName, SecretsVaultSchema.Name);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at_utc");
        builder.Property(entity => entity.RemovedAt).HasColumnName("removed_at_utc");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at_utc");
        builder.HasIndex(entity => new { entity.ProductId, entity.Environment, entity.Key })
            .IsUnique()
            .HasDatabaseName("ux_vault_secrets_key");
        builder.HasOne<ProductEntity>().WithMany().HasForeignKey(entity => entity.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
