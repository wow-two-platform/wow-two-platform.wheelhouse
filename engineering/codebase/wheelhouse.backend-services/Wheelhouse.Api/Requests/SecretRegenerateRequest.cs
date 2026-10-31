using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Names what a regeneration takes: an optional note on the new version, and the release whose declaration and
/// image make the value (else the one the target runs).</summary>
/// <param name="Note">The owner's note on the new version.</param>
/// <param name="Release">The release bundle; null for the one the target runs.</param>
public sealed record SecretRegenerateRequest([StringLength(300)] string? Note = null, [StringLength(200)] string? Release = null);
