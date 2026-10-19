using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the version a restore brings back.</summary>
/// <param name="Version">The version.</param>
/// <param name="Note">The owner's note on the restore.</param>
public sealed record SecretRestoreRequest([Range(1, int.MaxValue)] int Version, [StringLength(300)] string? Note = null);
