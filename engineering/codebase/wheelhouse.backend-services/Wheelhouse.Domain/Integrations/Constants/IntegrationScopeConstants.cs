namespace Wheelhouse.Domain.Integrations.Constants;

/// <summary>Holds the scopes an integration key can grant; a key reaches only what its scopes name.</summary>
public static class IntegrationScopeConstants
{
    /// <summary>Holds the scope that reads the product catalog: identities, lifecycle, environments, sites and vault
    /// namespaces.</summary>
    public const string CatalogRead = "catalog:read";

    /// <summary>Holds the scope for read-only MCP fleet, deployment and health observations.</summary>
    public const string DeploymentsRead = "deployments:read";

    /// <summary>Holds every scope a key can be created with.</summary>
    public static readonly IReadOnlyList<string> All = [CatalogRead, DeploymentsRead];
}
