using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a target in the runner's inventory.</summary>
public sealed record SnapshotTargetModel
{
    /// <summary>Gets the target's slug.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the slug of the server the target runs on.</summary>
    public required string ServerId { get; init; }

    /// <summary>Gets the slug of the product the target runs.</summary>
    public required string Product { get; init; }

    /// <summary>Gets which stage of the product the target runs.</summary>
    public required DeploymentEnvironment Environment { get; init; }

    /// <summary>Gets the shared Docker network.</summary>
    public required string Network { get; init; }

    /// <summary>Gets the folder releases are kept under.</summary>
    public required string Root { get; init; }

    /// <summary>Gets each service's settings file.</summary>
    public required IReadOnlyList<SnapshotSettingModel> Settings { get; init; }

    /// <summary>Gets the requests that prove a rollout.</summary>
    public required IReadOnlyList<SnapshotSmokeModel> Smoke { get; init; }

    /// <summary>Gets the hosts of named sites.</summary>
    public required IReadOnlyList<SnapshotSiteModel> Sites { get; init; }
}
