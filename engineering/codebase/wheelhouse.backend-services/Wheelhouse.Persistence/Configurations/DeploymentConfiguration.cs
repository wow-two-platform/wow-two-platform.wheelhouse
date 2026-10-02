using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the placeholder deployment entity and its deployments table.</summary>
public sealed class DeploymentConfiguration : IEntityTypeConfiguration<Deployment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Deployment> builder)
    {
        builder
            .ToTable(Deployment.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => new { entity.ProductId, entity.CreatedAt });
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
    }
}
