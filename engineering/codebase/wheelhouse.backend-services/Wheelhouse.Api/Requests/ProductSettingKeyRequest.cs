using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Declares a key an owner adds to a product.</summary>
/// <param name="Type">What its values must look like: text, number, boolean, choice, url, cron, date or email.</param>
/// <param name="Secret">Whether its values go to the vault.</param>
/// <param name="Required">Whether a deploy waits for a value.</param>
/// <param name="Format">A date key's format, such as <c>yyyy-MM-dd</c>.</param>
/// <param name="Choices">A choice key's values.</param>
/// <param name="Note">What the key is for.</param>
/// <param name="ConfirmPlain">Whether a key that reads as a secret is a plain setting all the same.</param>
public sealed record ProductSettingKeyRequest(
    [Required, StringLength(20)] string Type,
    bool Secret = false,
    bool Required = false,
    [StringLength(40)] string? Format = null,
    [MaxLength(50)] IReadOnlyList<string>? Choices = null,
    [StringLength(300)] string? Note = null,
    bool ConfirmPlain = false);
