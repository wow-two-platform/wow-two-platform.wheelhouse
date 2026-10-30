using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>A root key held in memory: set <see cref="Key"/> to null for a missing file.</summary>
public sealed class FixedMasterKeyBroker(byte[]? key) : IMasterKeyBroker
{
    public byte[]? Key { get; set; } = key;
    public string? Problem { get; set; }
    public int Version { get; set; }
    public string Location => "/fixed/root.key";

    /// <summary>What the file check finds while a key is present: null passes.</summary>
    public string? FileProblem { get; set; }

    public string? CheckAccess() => Key is null ? MasterKeyRead.Missing : FileProblem;

    public MasterKeyRead Read() => Key is null
        ? new MasterKeyRead { Problem = Problem ?? MasterKeyRead.Missing, Stamp = "" }
        : new MasterKeyRead { Key = (byte[])Key.Clone(), Stamp = "v" + Version };
}
