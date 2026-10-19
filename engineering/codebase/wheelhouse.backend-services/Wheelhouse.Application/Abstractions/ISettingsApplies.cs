using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the scheduled applies of edited settings, one per target.</summary>
public interface ISettingsApplies
{
    /// <summary>Gets a target's scheduled apply, or null.</summary>
    Task<SettingsApplyEntity?> GetAsync(Guid targetId, CancellationToken ct = default);

    /// <summary>Gets every apply due at <paramref name="now"/>, earliest first.</summary>
    Task<IReadOnlyList<SettingsApplyEntity>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Schedules a target's apply, replacing one it had.</summary>
    Task SetAsync(SettingsApplyEntity apply, CancellationToken ct = default);

    /// <summary>Drops a target's scheduled apply; false when it had none.</summary>
    Task<bool> RemoveAsync(Guid targetId, CancellationToken ct = default);
}
