using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the deployment intent entity and its deployment_intents table: slugs rather than keys, so a row
/// outlives its target, read in time order.</summary>
public sealed class DeploymentIntentEntityConfiguration : IEntityTypeConfiguration<DeploymentIntentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DeploymentIntentEntity> builder)
    {
        builder
            .ToTable(DeploymentIntentEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .Property(entity => entity.At)
            .HasColumnName("at_utc");
        builder
            .HasIndex(entity => entity.At)
            .HasDatabaseName("ix_deployment_intents_at");
    }
}
