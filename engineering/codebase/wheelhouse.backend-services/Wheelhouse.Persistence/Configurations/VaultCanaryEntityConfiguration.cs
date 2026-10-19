using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the vault canary: one row, proving the root key.</summary>
public sealed class VaultCanaryEntityConfiguration : IEntityTypeConfiguration<VaultCanaryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VaultCanaryEntity> builder)
    {
        builder.ToTable(VaultCanaryEntity.TableName, SecretsVaultSchema.Name);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
    }
}
