using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures a target's scheduled settings apply: one per target, gone with the target.</summary>
public sealed class SettingsApplyEntityConfiguration : IEntityTypeConfiguration<SettingsApplyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingsApplyEntity> builder)
    {
        builder.ToTable(SettingsApplyEntity.TableName);
        builder.HasKey(entity => entity.TargetId);
        builder.Property(entity => entity.TargetId).ValueGeneratedNever();
        builder.Property(entity => entity.At).HasColumnName("at_utc");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at_utc");
        builder.HasOne<TargetEntity>().WithMany().HasForeignKey(entity => entity.TargetId).OnDelete(DeleteBehavior.Cascade);
    }
}
