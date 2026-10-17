using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Workspaces.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the pin entity and its pins table: each pin names exactly the target its kind takes, an owner
/// pins a target once, and a pin goes with its workspace or product.</summary>
public sealed class PinEntityConfiguration : IEntityTypeConfiguration<PinEntity>
{
    /// <summary>Holds the check that a pin names exactly the target its kind takes.</summary>
    public const string TargetCheck =
        "(kind = 'workspace' AND workspace_id IS NOT NULL AND product_id IS NULL AND service IS NULL) OR " +
        "(kind = 'product' AND workspace_id IS NULL AND product_id IS NOT NULL AND service IS NULL) OR " +
        "(kind = 'service' AND workspace_id IS NULL AND product_id IS NOT NULL AND service IS NOT NULL)";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PinEntity> builder)
    {
        builder
            .ToTable(PinEntity.TableName, table => table.HasCheckConstraint("ck_pins_target", TargetCheck));
        builder
            .HasKey(entity => entity.Id);
        // Postgres compares the null parts of a target as equal, so a pin cannot be made twice.
        builder
            .HasIndex(entity => new { entity.Owner, entity.Kind, entity.WorkspaceId, entity.ProductId, entity.Service })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ix_pins_target");
        builder
            .HasIndex(entity => new { entity.Owner, entity.Order })
            .HasDatabaseName("ix_pins_owner_order");
        builder
            .Property(entity => entity.Order)
            .HasColumnName("sort_order");
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .HasOne<WorkspaceEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<ProductEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
