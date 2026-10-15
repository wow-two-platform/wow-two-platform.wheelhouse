using System.Text.Json;
using System.Text.Json.Serialization;
using Wheelhouse.Domain.Integrations.Constants;
using WoW.Two.Sdk.Backend.Beta.Ai.Mcp;
using WoW.Two.Sdk.Backend.Beta.Foundation.Serialization;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;

namespace Wheelhouse.Api.Mcp;

/// <summary>Exposes read-only Wheelhouse tools to scoped integration keys.</summary>
public static class WheelhouseMcpConfiguration
{
    /// <summary>Holds the endpoint policy requiring an integration key.</summary>
    public const string EndpointPolicy = "WheelhouseMcp";

    /// <summary>Holds the tool policy requiring catalog read access.</summary>
    public const string CatalogPolicy = "WheelhouseMcpCatalogRead";

    /// <summary>Holds the tool policy requiring infrastructure read access.</summary>
    public const string DeploymentsPolicy = "WheelhouseMcpDeploymentsRead";

    /// <summary>Registers explicit read-only tools and their integration-key policies.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static WebApplicationBuilder AddWheelhouseMcp(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(EndpointPolicy, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser())
            .AddPolicy(CatalogPolicy, policy => policy.RequireApiKeyScope(IntegrationScopeConstants.CatalogRead))
            .AddPolicy(DeploymentsPolicy, policy => policy.RequireApiKeyScope(IntegrationScopeConstants.DeploymentsRead));

        // The SDK wire preset (camelCase, string enums); nulls stay on the wire and dictionary keys keep their casing.
        var json = new JsonSerializerOptions(JsonOptionsConstants.Default)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            DictionaryKeyPolicy = null,
        };
        builder.Services.AddStatelessMcpServer().WithTools<WheelhouseReadTools>(json);
        return builder;
    }

    /// <summary>Maps the private MCP endpoint behind API-key authentication.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static WebApplication MapWheelhouseMcp(this WebApplication app)
    {
        app.MapAuthenticatedMcp("/mcp", EndpointPolicy);
        return app;
    }
}
