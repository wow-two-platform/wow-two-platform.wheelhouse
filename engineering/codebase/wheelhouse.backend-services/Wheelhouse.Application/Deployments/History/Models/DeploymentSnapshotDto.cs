namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents the restore point a prod rollout took before it changed anything: where it lies on the target,
/// what it holds and the commands that restore it. Never a credential: a restore reads the password from
/// <c>PGPASSWORD</c>.</summary>
/// <param name="Path">The snapshot's folder on the target.</param>
/// <param name="CreatedAt">When it was taken.</param>
/// <param name="Databases">Each database dumped (pg_dump custom format).</param>
/// <param name="Volumes">Each named volume archived.</param>
/// <param name="Restore">The commands that restore it, in order, to run on the target.</param>
public sealed record DeploymentSnapshotDto(
    string Path,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<SnapshotDatabaseDto> Databases,
    IReadOnlyList<SnapshotVolumeDto> Volumes,
    IReadOnlyList<string> Restore);
