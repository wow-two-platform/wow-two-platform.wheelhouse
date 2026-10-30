namespace Wheelhouse.Api.Requests;

/// <summary>Holds the path-safe vault namespace pattern for vault routes and bodies; a vault id is a name slug and a
/// secret key a configuration key (the be sdk <c>FormatPatternConstants.NameSlug</c> and <c>ConfigurationKey</c>).</summary>
public static class VaultRules
{
    /// <summary>A vault namespace slug.</summary>
    public const string Namespace = "^[a-z0-9][a-z0-9._-]{0,99}$";
}
