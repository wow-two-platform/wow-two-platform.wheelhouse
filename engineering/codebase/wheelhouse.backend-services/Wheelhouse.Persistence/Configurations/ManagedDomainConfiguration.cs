using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Domains.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the placeholder managed domain entity and its domains table.</summary>
public sealed class ManagedDomainConfiguration : IEntityTypeConfiguration<ManagedDomain>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ManagedDomain> builder)
    {
        builder
            .ToTable("domains");
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => entity.Name)
            .IsUnique();
    }
}
