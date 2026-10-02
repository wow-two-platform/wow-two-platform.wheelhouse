using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Audit.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the audit entry entity and its append-only audit_entries table.</summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder
            .ToTable(AuditEntry.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Sequence)
            .IsUnique();
        builder
            .HasIndex(entity => entity.OccurredAtUtc);
    }
}
