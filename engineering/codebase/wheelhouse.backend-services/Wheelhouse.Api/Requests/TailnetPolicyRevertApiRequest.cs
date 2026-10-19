using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the live policy's ETag the owner saw when choosing to revert.</summary>
public sealed record TailnetPolicyRevertApiRequest([Required, MaxLength(200)] string Etag);
