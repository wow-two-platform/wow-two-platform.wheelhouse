using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the auto-promotion entity and its auto_promotions table: one row per prod target, gone with it.</summary>
public sealed class AutoPromotionEntityConfiguration : IEntityTypeConfiguration<AutoPromotionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AutoPromotionEntity> builder)
    {
        builder
            .ToTable(AutoPromotionEntity.TableName);
        builder
            .HasKey(entity => entity.TargetId);
        builder
            .Property(entity => entity.AttemptedAt)
            .HasColumnName("attempted_at_utc");
        builder
            .HasOne<TargetEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.TargetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
