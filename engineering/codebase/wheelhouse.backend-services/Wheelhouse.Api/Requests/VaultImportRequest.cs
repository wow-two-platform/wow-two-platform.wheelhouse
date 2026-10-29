using System.ComponentModel.DataAnnotations;
using Wheelhouse.Application.Inventory.Constants;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the inventory vault an import reads from.</summary>
/// <param name="Vault">The vault's slug.</param>
public sealed record VaultImportRequest([Required, RegularExpression(InventoryPatternConstants.Slug)] string Vault);
