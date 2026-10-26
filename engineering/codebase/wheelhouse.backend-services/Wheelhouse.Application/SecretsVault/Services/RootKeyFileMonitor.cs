using Wheelhouse.Application.SecretsVault.Models;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>Keeps the last scheduled check of the root key file for the life of the process: the vault status shows it,
/// and a failure is an issue until a check passes again. The job checks at start, so a restart forgets nothing for
/// long. Holds no key.</summary>
public sealed class RootKeyFileMonitor
{
    private RootKeyFileCheckDto? _last;

    /// <summary>Gets the last check; null before the first.</summary>
    public RootKeyFileCheckDto? Last => Volatile.Read(ref _last);

    /// <summary>Records a check, which replaces the one before.</summary>
    /// <param name="check">The check.</param>
    public void Record(RootKeyFileCheckDto check) => Volatile.Write(ref _last, check);
}
