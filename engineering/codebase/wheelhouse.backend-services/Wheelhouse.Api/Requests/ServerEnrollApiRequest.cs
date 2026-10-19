using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Carries what an enrollment needs to reach a fresh server; none of it is secret.</summary>
/// <param name="PublicAddress">The server's public IPv4 address.</param>
/// <param name="SshPort">Its public SSH port.</param>
/// <param name="LoginUser">The account the image delivers and the one-time key was added to.</param>
/// <param name="Admin">The administrator the owner signs in as afterwards.</param>
/// <param name="AdminKey">The owner's SSH public key for a new administrator.</param>
/// <param name="TailnetHostname">The host's name on the tailnet.</param>
/// <param name="ExpectedHostKey">The host key fingerprint from the provider's console.</param>
public sealed record ServerEnrollApiRequest(
    [Required] string PublicAddress,
    int SshPort,
    [Required] string LoginUser,
    [Required] string Admin,
    [MaxLength(1000)] string? AdminKey,
    [Required] string TailnetHostname,
    [MaxLength(100)] string? ExpectedHostKey);
