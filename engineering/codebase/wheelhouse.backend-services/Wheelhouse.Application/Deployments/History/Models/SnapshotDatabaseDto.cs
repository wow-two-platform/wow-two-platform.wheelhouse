namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one database a snapshot dumped.</summary>
/// <param name="Service">The service whose settings name it.</param>
/// <param name="Host">The database host.</param>
/// <param name="Port">The database port.</param>
/// <param name="Database">The database's name.</param>
/// <param name="File">The dump's file name in the snapshot folder.</param>
/// <param name="Bytes">The dump's size.</param>
public sealed record SnapshotDatabaseDto(string? Service, string Host, int Port, string Database, string File, long? Bytes);
