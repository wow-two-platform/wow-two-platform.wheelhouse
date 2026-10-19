namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents one read of the root key.</summary>
/// <param name="Key">The 32-byte key; null when there is none. The caller must not keep or log it.</param>
/// <param name="Problem">Why there is no key: <c>missing</c> or <c>invalid</c>; null when there is one.</param>
/// <param name="Stamp">What identifies this file's content without its value (time and size), so a check made once
/// holds until the file changes.</param>
public sealed record VaultKeyRead(byte[]? Key, string? Problem, string Stamp)
{
    /// <summary>The problem code of a missing key file.</summary>
    public const string Missing = "missing";

    /// <summary>The problem code of a key file that holds no base64 256-bit key.</summary>
    public const string Invalid = "invalid";

    /// <summary>Returns the read without its key, so logs cannot print one.</summary>
    public override string ToString() => nameof(VaultKeyRead) + " { " + (Problem ?? "present") + " }";
}
