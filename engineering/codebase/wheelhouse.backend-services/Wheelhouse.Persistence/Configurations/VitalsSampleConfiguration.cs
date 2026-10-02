using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Operations.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Configures the vitals sample entity and its vitals_samples table.</summary>
public sealed class VitalsSampleConfiguration : IEntityTypeConfiguration<VitalsSample>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VitalsSample> builder)
    {
        builder
            .ToTable(VitalsSample.TableName);
        builder
            .HasKey(entity => entity.Id);
        builder
            .HasIndex(entity => new { entity.TargetId, entity.SampledAtUtc });
        builder
            .HasIndex(entity => entity.SampledAtUtc);
    }
}
