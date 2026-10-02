namespace Wheelhouse.Tests.E2E.Support;

/// <summary>Response shape for a server (mirrors the host's <c>ServerDto</c> — enum read as its string name).</summary>
/// <param name="Slug">The server's slug.</param>
/// <param name="Name">Display name.</param>
/// <param name="Provider">Who hosts it (string-serialized enum).</param>
/// <param name="Host">The host SSH connects to.</param>
/// <param name="Region">The provider's region.</param>
/// <param name="SshUser">The user SSH signs in as.</param>
/// <param name="SshPort">The port SSH connects to.</param>
public sealed record ServerResponse(
    string Slug,
    string Name,
    string Provider,
    string Host,
    string Region,
    string SshUser,
    int SshPort);

/// <summary>Response shape for a target (mirrors the host's <c>TargetDto</c> — enum read as its string name).</summary>
/// <param name="Slug">The target's slug.</param>
/// <param name="Product">Its product's slug.</param>
/// <param name="Server">Its server's slug.</param>
/// <param name="Environment">Its stage (string-serialized enum).</param>
/// <param name="Network">Its shared network.</param>
/// <param name="Root">Its releases folder.</param>
public sealed record TargetResponse(string Slug, string Product, string Server, string Environment, string Network, string Root);

/// <summary>Response shape for a vault definition (mirrors the host's <c>VaultDto</c>).</summary>
/// <param name="Slug">The vault's slug.</param>
/// <param name="Name">Display name.</param>
/// <param name="Server">Its server's slug.</param>
/// <param name="Url">Its management endpoint.</param>
public sealed record VaultResponse(string Slug, string Name, string Server, string Url);

/// <summary>Response shape for a catalog product (mirrors the host's <c>ProductDto</c> — enum read as its string name).</summary>
/// <param name="Slug">The product's identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What the product does.</param>
/// <param name="Lifecycle">Where it stands (string-serialized enum).</param>
/// <param name="Repository">Its source repository.</param>
/// <param name="IconUrl">The path of its icon.</param>
/// <param name="Environments">Its environments, dev to prod.</param>
public sealed record ProductResponse(
    string Slug,
    string Name,
    string Description,
    string Lifecycle,
    ProductRepositoryResponse Repository,
    string IconUrl,
    IReadOnlyList<ProductEnvironmentResponse> Environments);

/// <summary>Response shape for a catalog product's repository.</summary>
/// <param name="Name">The repository, as <c>owner/name</c>.</param>
/// <param name="Url">Its GitHub address.</param>
/// <param name="DefaultBranch">The branch releases come from.</param>
public sealed record ProductRepositoryResponse(string Name, string Url, string DefaultBranch);

/// <summary>Response shape for one environment of a catalog product.</summary>
/// <param name="Name">The environment.</param>
/// <param name="Sites">The sites it publishes.</param>
/// <param name="Secrets">Where its settings belong, when a vault serves it.</param>
public sealed record ProductEnvironmentResponse(
    string Name,
    IReadOnlyList<ProductSiteResponse> Sites,
    ProductSecretsResponse? Secrets);

/// <summary>Response shape for a site an environment publishes.</summary>
/// <param name="Name">The site's name.</param>
/// <param name="Url">Its address.</param>
/// <param name="Exposure"><c>public</c> or <c>private</c>.</param>
public sealed record ProductSiteResponse(string Name, string Url, string Exposure);

/// <summary>Response shape for where an environment's settings belong in a vault.</summary>
/// <param name="Vault">The vault.</param>
/// <param name="Namespace">The namespace.</param>
public sealed record ProductSecretsResponse(string Vault, string Namespace);

/// <summary>Response shape for an integration key (mirrors the host's <c>IntegrationKeyDto</c>).</summary>
/// <param name="Id">The key's identifier.</param>
/// <param name="Name">Its name.</param>
/// <param name="Prefix">The secret's first characters.</param>
/// <param name="Scopes">What it may reach.</param>
/// <param name="CreatedBy">Who created it.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="LastUsedAt">When it last authenticated a request.</param>
/// <param name="RevokedAt">When it was revoked.</param>
public sealed record IntegrationKeyResponse(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>Response shape for a new integration key and its one-time secret.</summary>
/// <param name="Key">The key as kept.</param>
/// <param name="Secret">The secret, shown once.</param>
public sealed record IntegrationKeyWithSecretResponse(IntegrationKeyResponse Key, string Secret);
