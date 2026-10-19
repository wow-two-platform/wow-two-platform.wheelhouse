namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents one version of a standalone vault's secret.</summary>
/// <param name="Version">Its number.</param>
/// <param name="DataKeyId">The data key that encrypted it.</param>
/// <param name="Value">The encrypted value.</param>
/// <param name="CreatedBy">Who wrote it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record StandaloneVersion(int Version, Guid DataKeyId, StandaloneSealed Value, string CreatedBy, DateTimeOffset CreatedAt);
