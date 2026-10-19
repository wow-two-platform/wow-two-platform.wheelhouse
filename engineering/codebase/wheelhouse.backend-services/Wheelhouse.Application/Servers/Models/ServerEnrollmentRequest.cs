namespace Wheelhouse.Application.Servers.Models;

/// <summary>Represents what an enrollment needs to reach a fresh server: none of it secret.</summary>
/// <param name="Server">The server's slug.</param>
/// <param name="PublicAddress">The server's public IP address, as the provider shows it.</param>
/// <param name="SshPort">Its public SSH port.</param>
/// <param name="LoginUser">The account the image delivers and the one-time key was added to: <c>root</c>, or an
/// administrator such as <c>ubuntu</c> with passwordless sudo.</param>
/// <param name="Admin">The administrator the owner signs in as once root sign-in is off.</param>
/// <param name="AdminKey">The owner's SSH public key for a new administrator; unused when it already exists.</param>
/// <param name="TailnetHostname">The host's name on the tailnet.</param>
/// <param name="ExpectedHostKey">The host key fingerprint the provider's console shows, or <c>null</c> to let the
/// tailnet prove the host instead.</param>
public sealed record ServerEnrollmentRequest(
    string Server, string PublicAddress, int SshPort, string LoginUser, string Admin, string? AdminKey,
    string TailnetHostname, string? ExpectedHostKey);
