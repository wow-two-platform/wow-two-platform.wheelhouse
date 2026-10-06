using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Mcp;
using WoW.Two.Sdk.Backend.Beta.Meta;
using WoW.Two.Sdk.Backend.Beta.Web.Hosting;

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
            .AddAuthentication()
            .AddApiServices()
            .AddWheelhouseMcp();

        return builder;
    }

    /// <summary>Configures the middleware pipeline and maps endpoints.</summary>
    public static WebApplication Configure(this WebApplication app)
    {
        // Serve the Vue workspace from wwwroot (single-deploy: API + UI in one host) via the SDK SPA-hosting helper.
        // Static assets stay public so the sign-in screen loads before auth, and are registered before the SDK pipeline
        // so they short-circuit.
        app.UseSpaHosting();

        // SDK pipeline: forwarded headers, secure headers, response compression; maps OpenAPI (dev) + the SDK health probe.
        app.UseApiDefaults();

        // Auth after static files + the SDK pipeline, before endpoints — the fallback policy gates the controllers while
        // the SPA shell and the anonymous /health below stay reachable.
        app.UseWheelhouseAuth();

        app.MapControllers();
        app.MapWheelhouseMcp();

        // SDK SPA fallback: an unmatched /api/* 404s as JSON (never falls through to the SPA shell — an HTML body for an
        // API path is cacheable and breaks clients), every other unmatched route falls back to index.html. Both anonymous.
        app.MapSpaFallback();

        return app;
    }
}
