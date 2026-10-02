using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Vaults.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the vault entity and its vaults table; a vault holds its server in place.</summary>
public sealed class VaultEntityConfiguration : IEntityTypeConfiguration<VaultEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VaultEntity> builder)
    {
        builder
            .ToTable(VaultEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Slug)
            .IsUnique();
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
        builder
            .HasOne<ServerEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ServerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
