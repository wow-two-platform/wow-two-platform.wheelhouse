using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the fleet rollout entity and its fleet_rollouts table: its products go with it.</summary>
public sealed class FleetRolloutEntityConfiguration : IEntityTypeConfiguration<FleetRolloutEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FleetRolloutEntity> builder)
    {
        builder
            .ToTable(FleetRolloutEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .Property(entity => entity.CancelRequestedAt)
            .HasColumnName("cancel_requested_at_utc");
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.CompletedAt)
            .HasColumnName("completed_at_utc");
        builder
            .HasMany(entity => entity.Items)
            .WithOne()
            .HasForeignKey(item => item.FleetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
