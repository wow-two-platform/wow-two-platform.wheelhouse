using System.Text.Json;
using System.Text.Json.Serialization;
using Wheelhouse.Domain.Integrations.Constants;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Ai.Mcp;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Wheelhouse.Api.Mcp;

/// <summary>Exposes read-only Wheelhouse tools to scoped integration keys.</summary>
public static class WheelhouseMcpConfiguration
{
    /// <summary>Holds the endpoint's path.</summary>
    public const string Path = "/mcp";

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
        // Bound when first resolved, so a test host's configuration applies; a malformed origin fails the boot.
        var configuration = builder.Configuration;
        builder.Services.AddValidatedOptions<McpSettings>(
            settings => configuration.GetSection("Mcp").Bind(settings),
            options => options.Validate(
                settings => settings.AllowedOrigins.All(origin => McpSettings.NormalizeOrigin(origin) is not null),
                "Mcp:AllowedOrigins must list browser origins as scheme://host[:port] over http or https."));

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
        builder.Services.AddStatelessMcpServer()
            .WithTools<WheelhouseReadTools>(json)
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpToolErrorFilter.Apply));
        return builder;
    }

    /// <summary>Adds the endpoint's origin and body-size screen; place it before authentication.</summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The application pipeline.</returns>
    public static IApplicationBuilder UseWheelhouseMcpGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<McpRequestGuard>();

    /// <summary>Maps the private MCP endpoint behind API-key authentication.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static WebApplication MapWheelhouseMcp(this WebApplication app)
    {
        app.MapAuthenticatedMcp(Path, EndpointPolicy);
        return app;
    }
}
