namespace Wheelhouse.Application.SecretsVault.Constants;

/// <summary>Names the built-in vault's states and why it is sealed, as the console and the API name them.</summary>
public static class SecretsVaultStates
{
    /// <summary>The root key is present and proven by the canary: secrets read and write.</summary>
    public const string Unsealed = "unsealed";

    /// <summary>No usable root key: metadata reads, values neither read nor write, and a deploy that needs secrets
    /// waits.</summary>
    public const string Sealed = "sealed";

    /// <summary>The root key file is not where the setting says.</summary>
    public const string Missing = "missing";

    /// <summary>The root key file holds no base64 256-bit key.</summary>
    public const string Invalid = "invalid";

    /// <summary>The root key decrypts neither the canary nor the product keys: it belongs to another vault.</summary>
    public const string Mismatch = "mismatch";
}
