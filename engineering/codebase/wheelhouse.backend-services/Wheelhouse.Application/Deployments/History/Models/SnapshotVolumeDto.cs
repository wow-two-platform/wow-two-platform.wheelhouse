namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one named volume a snapshot archived.</summary>
/// <param name="Volume">The Docker volume's name.</param>
/// <param name="File">The archive's file name in the snapshot folder.</param>
/// <param name="Bytes">The archive's size.</param>
public sealed record SnapshotVolumeDto(string Volume, string File, long? Bytes);
