namespace Wheelhouse.Application.InfraJobs;

/// <summary>Holds the shapes infrastructure requests take that the SDK validation vocabulary does not carry — the same
/// ones the runner enforces again. Addresses, host names, fingerprints, keys, digests and the account a fresh image
/// delivers take the be sdk <c>FormatPatternConstants</c>.</summary>
public static class InfraPatterns
{
    /// <summary>Holds the shape of the administrator's name.</summary>
    public const string Admin = "^[a-z][a-z0-9-]{0,30}$";

    /// <summary>Holds the shape of a site node's name; dev's adds <c>-dev</c>.</summary>
    public const string Node = "^[a-z][a-z0-9-]{0,50}[a-z0-9]$";

    /// <summary>The administrator names an enrollment refuses: root, and the deploy account the runner signs in as.</summary>
    public static readonly string[] ReservedAdmins = ["root", "wheelhouse-deploy"];
}
