namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents AES-GCM ciphertext as the standalone vault stores it.</summary>
/// <param name="CipherText">The ciphertext.</param>
/// <param name="Nonce">The nonce.</param>
/// <param name="Tag">The tag.</param>
public sealed record StandaloneSealed(byte[] CipherText, byte[] Nonce, byte[] Tag)
{
    /// <summary>Returns the record without its bytes.</summary>
    public override string ToString() => nameof(StandaloneSealed);
}
