using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the review the owner applies: the live policy's ETag and the rendered policy's digest it showed.</summary>
public sealed record TailnetPolicyApplyApiRequest([Required, MaxLength(200)] string Etag, [Required] string Digest);
