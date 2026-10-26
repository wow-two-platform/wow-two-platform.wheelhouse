using System.Text.Json;
using System.Text.Json.Serialization;
using Wheelhouse.Domain.Integrations.Constants;
using WoW.Two.Sdk.Beta.Be.Ai.Mcp;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Wheelhouse.Api.Mcp;

/// <summary>Exposes read-only Wheelhouse tools to scoped integration keys.</summary>
public static class WheelhouseMcpConfiguration
{
    /// <summary>Holds the endpoint's path.</summary>
    public const string Path = "/mcp";

    /// <summary>Holds the configuration section bound into the SDK's <see cref="McpRequestGuardOptions"/>:
    /// <c>AllowedOrigins</c> (<c>scheme://host[:port]</c>, empty by default) and <c>MaxRequestBodyBytes</c> (64 KiB).</summary>
    public const string SettingsSection = "Mcp";

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
        // The SDK guard screens every MapAuthenticatedMcp request: a foreign browser Origin gets 403, a body over 64 KiB 413.
        // Mcp:AllowedOrigins (and Mcp:MaxRequestBodyBytes) bind into it when first resolved, so a test host's or a
        // console's settings apply; a malformed origin fails the boot. The SDK tool error filter answers failed calls.
        builder.Services.AddStatelessMcpServer().WithTools<WheelhouseReadTools>(json);
        builder.Services.Configure<McpRequestGuardOptions>(builder.Configuration.GetSection(SettingsSection));
        return builder;
    }

    /// <summary>Maps the private MCP endpoint behind API-key authentication and the SDK request guard.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static WebApplication MapWheelhouseMcp(this WebApplication app)
    {
        app.MapAuthenticatedMcp(Path, EndpointPolicy);
        return app;
    }
}
