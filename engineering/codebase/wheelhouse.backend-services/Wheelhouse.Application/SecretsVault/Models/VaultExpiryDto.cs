namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents a secret that lapses soon or has lapsed, for the Issues panel.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Environment">The environment.</param>
/// <param name="Target">The environment's target, when the inventory has one.</param>
/// <param name="Key">The configuration path.</param>
/// <param name="ExpiresAt">When it lapses.</param>
/// <param name="Level"><c>expired</c>, <c>7d</c> or <c>30d</c>.</param>
public sealed record VaultExpiryDto(
    string Product, string Environment, string? Target, string Key, DateTimeOffset ExpiresAt, string Level);
