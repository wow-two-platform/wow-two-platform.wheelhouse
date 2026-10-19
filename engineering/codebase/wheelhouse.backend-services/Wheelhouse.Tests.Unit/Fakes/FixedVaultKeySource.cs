using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>A root key held in memory: set <see cref="Key"/> to null for a missing file.</summary>
public sealed class FixedVaultKeySource(byte[]? key) : IVaultKeySource
{
    public byte[]? Key { get; set; } = key;
    public string? Problem { get; set; }
    public int Version { get; set; }
    public string Location => "/fixed/root.key";

    public VaultKeyRead Read() => Key is null
        ? new VaultKeyRead(null, Problem ?? VaultKeyRead.Missing, "")
        : new VaultKeyRead((byte[])Key.Clone(), null, "v" + Version);
}
