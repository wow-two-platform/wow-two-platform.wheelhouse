using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures what each deploy rendered, by job; the target is a slug, so a row outlives its target.</summary>
public sealed class DeploymentRenderEntityConfiguration : IEntityTypeConfiguration<DeploymentRenderEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DeploymentRenderEntity> builder)
    {
        builder.ToTable(DeploymentRenderEntity.TableName);
        builder.HasKey(entity => entity.JobId);
        builder.Property(entity => entity.JobId).ValueGeneratedNever();
        builder.Property(entity => entity.At).HasColumnName("at_utc");
        builder.HasIndex(entity => new { entity.Target, entity.At }).HasDatabaseName("ix_deployment_renders_target");
    }
}
