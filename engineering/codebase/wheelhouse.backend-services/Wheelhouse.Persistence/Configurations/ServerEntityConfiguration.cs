using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Servers.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Json;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the server entity and its servers table.</summary>
public sealed class ServerEntityConfiguration : IEntityTypeConfiguration<ServerEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServerEntity> builder)
    {
        builder
            .ToTable(ServerEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Slug)
            .IsUnique();
        builder
            .Property(entity => entity.Ingress)
            .HasColumnType("jsonb")
            .HasJsonConversion();
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
    }
}
