using System.Globalization;
using System.Security.Cryptography;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Infrastructure.SecretsVault;

/// <summary>Reads the built-in vault's root key from a file: one line of base64 holding 256 bits, the same form as the
/// standalone vault's master key, so the owner may keep that key. Each read takes the file as it is now — placed after
/// start, it unseals without a restart; removed, the vault seals — and returns a fresh copy the caller clears.</summary>
/// <param name="path">The file's absolute path.</param>
public sealed class FileVaultKeySource(string path) : IVaultKeySource
{
    /// <inheritdoc />
    public string Location => path;

    /// <inheritdoc />
    public VaultKeyRead Read()
    {
        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
                return new VaultKeyRead(null, VaultKeyRead.Missing, "");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new VaultKeyRead(null, VaultKeyRead.Missing, "");
        }
        var stamp = file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + file.Length.ToString(CultureInfo.InvariantCulture);
        if (file.Length is 0 or > 1024)
            return new VaultKeyRead(null, VaultKeyRead.Invalid, stamp);
        char[]? text = null;
        try
        {
            text = File.ReadAllText(path).Trim().ToCharArray();
            var key = new byte[64];
            if (!Convert.TryFromBase64Chars(text, key, out var written) || written != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                return new VaultKeyRead(null, VaultKeyRead.Invalid, stamp);
            }
            var exact = key[..32];
            CryptographicOperations.ZeroMemory(key);
            return new VaultKeyRead(exact, null, stamp);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file Wheelhouse may not read is as good as missing: the owner fixes its mode or owner.
            return new VaultKeyRead(null, VaultKeyRead.Missing, stamp);
        }
        finally
        {
            if (text is not null)
                Array.Clear(text);
        }
    }
}
