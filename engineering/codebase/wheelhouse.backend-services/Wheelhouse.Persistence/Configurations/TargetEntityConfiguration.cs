using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Json;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the target entity and its targets table; a target holds its product and server in place.</summary>
public sealed class TargetEntityConfiguration : IEntityTypeConfiguration<TargetEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TargetEntity> builder)
    {
        builder
            .ToTable(TargetEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Slug)
            .IsUnique();
        builder
            .Property(entity => entity.Settings)
            .HasColumnType("jsonb")
            .HasJsonConversion();
        builder
            .Property(entity => entity.SmokeChecks)
            .HasColumnType("jsonb")
            .HasJsonConversion();
        builder
            .Property(entity => entity.Sites)
            .HasColumnType("jsonb")
            .HasJsonConversion();
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
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ServerEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ServerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
