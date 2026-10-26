using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Mcp;
using WoW.Two.Sdk.Beta.Be.Meta;
using WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;

namespace Wheelhouse.Api.Configurations;

/// <summary>Configures the Wheelhouse host — a slim orchestrator that delegates to per-concern extensions.</summary>
public static class HostConfiguration
{
    /// <summary>Registers services across all layers.</summary>
    public static WebApplicationBuilder Configure(this WebApplicationBuilder builder)
    {
        // Local-only dev overrides first — visible to AddApiDefaults' config reads.
        builder.AddSettings();

        builder.AddPlatformDefaults();

        builder
            .AddPersistenceLayer()
            .AddInfrastructureLayer()
            .AddApplicationLayer()
            .AddSelfDeploy()
            .AddAuthentication()
            .AddApiServices()
            .AddWheelhouseMcp();

        return builder;
    }

    /// <summary>Configures the middleware pipeline and maps endpoints.</summary>
    public static WebApplication Configure(this WebApplication app)
    {
        // Outermost: every error status, MVC error result and thrown exception leaves as a ProblemDetails document.
        app.UseHttpProblemDetails();

        // SDK pipeline: forwarded headers, secure headers, response compression, then the Vue workspace from wwwroot
        // (single deploy: API + UI in one host) before routing, so its public assets carry the headers and encoding and
        // load before sign-in. Auth runs after routing and before idempotency, so keys are scoped per caller; the SDK's
        // MCP guard screens /mcp after it. The fallback policy gates the controllers while the SPA
        // shell and the anonymous /health stay reachable. Maps OpenAPI (dev), the health probe and the SPA fallback:
        // an unmatched /api/* 404s as JSON, any other unmatched route serves index.html; fallbacks rank after the
        // endpoints below.
        app.UseApiDefaults(useIdentity: pipeline => pipeline.UseWheelhouseAuth());

        app.MapControllers();
        app.MapWheelhouseMcp();

        // /.well-known/* is a discovery namespace: MCP clients probe OAuth metadata there, so a miss must be a real 404,
        // never the SPA shell. The SDK SPA fallback reserves only /api; this literal prefix outranks its catch-all.
        app.MapFallback("/.well-known/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found"))
            .AllowAnonymous();

        return app;
    }
}
