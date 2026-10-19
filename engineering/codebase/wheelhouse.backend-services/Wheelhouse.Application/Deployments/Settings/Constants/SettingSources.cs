namespace Wheelhouse.Application.Deployments.Settings.Constants;

/// <summary>Names where a deploy's render takes a setting's value from, as the deploy dialog shows it.</summary>
public static class SettingSources
{
    /// <summary>The render derives it from the target and release: hosts, proxies, site URLs, the target's database.</summary>
    public const string Derived = "derived";

    /// <summary>A secret value in the environment's vault namespace; never shown.</summary>
    public const string Vault = "vault";

    /// <summary>A value in Wheelhouse's settings store.</summary>
    public const string Stored = "stored";

    /// <summary>The release's default for an optional key, from the image's own settings.</summary>
    public const string Default = "default";

    /// <summary>The target's host file may set it; the target check confirms it.</summary>
    public const string Host = "host";

    /// <summary>A required key no layer sets: the deploy waits for it.</summary>
    public const string Missing = "missing";

    /// <summary>An optional key without a value or default: the service's own default applies.</summary>
    public const string Unset = "unset";
}
