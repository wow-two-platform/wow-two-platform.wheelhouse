namespace Wheelhouse.Application.InfraJobs;

/// <summary>Holds the shapes infrastructure requests take — the same ones the runner enforces again; the account a fresh
/// image delivers takes <see cref="Inventory.Constants.InventoryPatternConstants.SshUser"/>.</summary>
public static class InfraPatterns
{
    /// <summary>Holds the shape of a public IPv4 address.</summary>
    public const string PublicAddress = @"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$";

    /// <summary>Holds the shape of the administrator's name.</summary>
    public const string Admin = "^[a-z][a-z0-9-]{0,30}$";

    /// <summary>Holds the shape of a tailnet host or site node name.</summary>
    public const string Hostname = "^[a-z][a-z0-9-]{0,61}[a-z0-9]$";

    /// <summary>Holds the shape of a site node's name; dev's adds <c>-dev</c>.</summary>
    public const string Node = "^[a-z][a-z0-9-]{0,50}[a-z0-9]$";

    /// <summary>Holds the shape of an SSH host key fingerprint, as <c>ssh-keygen -l</c> prints it.</summary>
    public const string Fingerprint = "^SHA256:[A-Za-z0-9+/]{43}=?$";

    /// <summary>Holds the shape of one SSH public key line.</summary>
    public const string PublicKey = @"^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)) [A-Za-z0-9+/]+=*( [^\s]{1,100})?$";

    /// <summary>Holds the shape of a policy digest: SHA-256, hex.</summary>
    public const string Digest = "^[a-f0-9]{64}$";

    /// <summary>The administrator names an enrollment refuses: root, and the deploy account the runner signs in as.</summary>
    public static readonly string[] ReservedAdmins = ["root", "wheelhouse-deploy"];
}
