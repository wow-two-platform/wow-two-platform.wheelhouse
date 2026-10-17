using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Workspaces.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the workspace entity and its workspaces table.</summary>
public sealed class WorkspaceEntityConfiguration : IEntityTypeConfiguration<WorkspaceEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkspaceEntity> builder)
    {
        builder
            .ToTable(WorkspaceEntity.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Slug)
            .IsUnique();
        builder
            .Property(entity => entity.Order)
            .HasColumnName("sort_order");
        builder
            .Property(entity => entity.CreatedAt)
            .HasColumnName("created_at_utc");
        builder
            .Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at_utc");
    }
}
