using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the owner's offline root key copy checks, newest first by when they were marked.</summary>
public sealed class VaultRootKeyVerificationEntityConfiguration : IEntityTypeConfiguration<VaultRootKeyVerificationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VaultRootKeyVerificationEntity> builder)
    {
        builder.ToTable(VaultRootKeyVerificationEntity.TableName, SecretsVaultSchema.Name);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.VerifiedAt).HasColumnName("verified_at_utc");
        builder.HasIndex(entity => entity.VerifiedAt).HasDatabaseName("ix_vault_root_key_verifications_at");
    }
}
