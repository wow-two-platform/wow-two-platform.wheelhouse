using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the queued deployment entity and its deployment_queue table: at most one per target, gone with it.</summary>
public sealed class QueuedDeploymentEntityConfiguration : IEntityTypeConfiguration<QueuedDeploymentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<QueuedDeploymentEntity> builder)
    {
        builder
            .ToTable(QueuedDeploymentEntity.TableName);
        builder
            .HasKey(entity => entity.TargetId);
        builder
            .Property(entity => entity.QueuedAt)
            .HasColumnName("queued_at_utc");
        builder
            .HasOne<TargetEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.TargetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
