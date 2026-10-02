using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Secrets.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the placeholder secret entity and its secrets table.</summary>
public sealed class SecretEntryConfiguration : IEntityTypeConfiguration<SecretEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SecretEntry> builder)
    {
        builder
            .ToTable("secrets");
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => new { entity.Scope, entity.RefId, entity.Key })
            .IsUnique();
    }
}
