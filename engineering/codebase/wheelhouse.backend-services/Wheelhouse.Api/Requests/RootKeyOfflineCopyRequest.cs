namespace Wheelhouse.Api.Requests;

/// <summary>Marks the owner's offline copy of the root key verified against the key in use.</summary>
/// <param name="Fingerprint">The key in use's fingerprint as the console showed it; the key itself is never sent.</param>
/// <param name="Reason">How the owner checked it; one line, 300 characters at most.</param>
public sealed record RootKeyOfflineCopyRequest(string? Fingerprint = null, string? Reason = null);
