using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures one product of a fleet rollout and its fleet_rollout_items table, keyed by fleet and place.</summary>
public sealed class FleetRolloutItemEntityConfiguration : IEntityTypeConfiguration<FleetRolloutItemEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FleetRolloutItemEntity> builder)
    {
        builder
            .ToTable(FleetRolloutItemEntity.TableName);
        builder
            .HasKey(entity => new { entity.FleetId, entity.Position });
        builder
            .Property(entity => entity.Position)
            .ValueGeneratedNever();
        builder
            .Property(entity => entity.StartedAt)
            .HasColumnName("started_at_utc");
        builder
            .Property(entity => entity.CompletedAt)
            .HasColumnName("completed_at_utc");
    }
}
