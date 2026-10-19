using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures a secret's versions: numbered once each, gone with the secret.</summary>
public sealed class VaultSecretVersionEntityConfiguration : IEntityTypeConfiguration<VaultSecretVersionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VaultSecretVersionEntity> builder)
    {
        builder.ToTable(VaultSecretVersionEntity.TableName, SecretsVaultSchema.Name);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
        builder.HasIndex(entity => new { entity.SecretId, entity.Version })
            .IsUnique()
            .HasDatabaseName("ux_vault_secret_versions_version");
        builder.HasOne<VaultSecretEntity>().WithMany().HasForeignKey(entity => entity.SecretId).OnDelete(DeleteBehavior.Cascade);
    }
}
