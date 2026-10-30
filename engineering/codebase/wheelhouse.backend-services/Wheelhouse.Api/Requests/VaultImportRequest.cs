using System.ComponentModel.DataAnnotations;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the inventory vault an import reads from.</summary>
/// <param name="Vault">The vault's slug.</param>
public sealed record VaultImportRequest([Required, RegularExpression(FormatPatternConstants.NameSlug)] string Vault);
