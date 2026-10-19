using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Configurations;

/// <summary>Holds the schema of the built-in Secrets Vault's tables, apart from the control plane's own.</summary>
public static class SecretsVaultSchema
{
    /// <summary>The schema name; SQLite test databases ignore it, so table names stay unique on their own.</summary>
    public const string Name = "vault";
}
