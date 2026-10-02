using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Integrations.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the integration key entity and its integration_keys table.</summary>
public sealed class IntegrationKeyEntityConfiguration : IEntityTypeConfiguration<IntegrationKeyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IntegrationKeyEntity> builder)
    {
        builder
            .ToTable(IntegrationKeyEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Hash)
            .IsUnique();
        builder
            .Property(entity => entity.LastUsedAt)
            .HasColumnName("last_used_at_utc");
        builder
            .Property(entity => entity.RevokedAt)
            .HasColumnName("revoked_at_utc");
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
        builder
            .Ignore(entity => entity.ScopeList);
    }
}
