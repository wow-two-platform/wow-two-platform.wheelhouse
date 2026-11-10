using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the other product's key a setting key links to, in the same environment.</summary>
/// <param name="SourceTarget">A target of the other product, such as <c>ocharo-dev</c>.</param>
/// <param name="SourceKey">The source key, such as <c>Identity:Server:Clients:8:ClientSecret</c>.</param>
public sealed record SettingLinkRequest([Required, StringLength(100)] string SourceTarget, [Required, StringLength(1100)] string SourceKey);
