using Wheelhouse.Application;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Persistence;
using Wheelhouse.Persistence.Repositories;
using WoW.Two.Sdk.Backend.Beta.Data;
using WoW.Two.Sdk.Backend.Beta.Foundation.Audit;
using WoW.Two.Sdk.Backend.Beta.Foundation.Time;
using WoW.Two.Sdk.Backend.Beta.Foundation.Validation;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;
using WoW.Two.Sdk.Backend.Beta.Integrations;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Validation;
using WoW.Two.Sdk.Backend.Beta.Meta;
using WoW.Two.Sdk.Backend.Beta.Web.Json;

namespace Wheelhouse.Api.Configurations;

/// <summary>Per-concern host registration extensions for the Wheelhouse API.</summary>
public static class HostConfigurationExtensions
{
    /// <summary>Loads optional local settings overrides (gitignored).</summary>
    public static WebApplicationBuilder AddSettings(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
        builder.AddDeploymentHosting();
        return builder;
    }

    /// <summary>Configures the SDK boot floor for a single-user control plane — output cache, rate limiting, and OTLP export off; OpenAPI in development only.</summary>
    public static WebApplicationBuilder AddPlatformDefaults(this WebApplicationBuilder builder)
    {
        builder.AddApiDefaults(o =>
        {
            o.ServiceName = "wheelhouse";
            o.EnableOutputCache = false;
            o.EnableRateLimiting = false;
            o.EnableOtlpExporters = false;
            o.ExposeOpenApi = builder.Environment.IsDevelopment();
        });
        return builder;
    }

    /// <summary>Registers the persistence layer — the SDK one-call Postgres bundle (data source, connection factory, audit interceptor, snake_case EF context, embedded bespoke migrator) plus the Wheelhouse stores.</summary>
    public static WebApplicationBuilder AddPersistenceLayer(this WebApplicationBuilder builder)
    {
        // One-call host floor for the context: resolves the connection string, builds the shared NpgsqlDataSource,
        // registers the Dapper connection factory + audit interceptor, adds the snake_case EF context, and wires the
        // embedded bespoke migrator over typeof(WheelhouseDbContext).Assembly. Keep Wheelhouse's existing config key
        // (ConnectionStrings:Wheelhouse) instead of the SDK default (DatabaseOptions:ConnectionString); env DB_CONNECTION still overrides.
        builder.Services.AddPostgresPersistence<WheelhouseDbContext>(
            builder.Configuration,
            o => o.ConnectionStringConfigKey = WheelhouseDatabase.ConnectionStringConfigKey);

        builder.Services.AddScoped<IProductsRepository, ProductsRepository>();
        builder.Services.AddScoped<IServersRepository, ServersRepository>();
        builder.Services.AddScoped<ITargetsRepository, TargetsRepository>();
        builder.Services.AddScoped<IVaultsRepository, VaultsRepository>();
        builder.Services.AddScoped<IIntegrationKeysRepository, IntegrationKeysRepository>();
        builder.Services.AddScoped<IApiKeyRepository, IntegrationKeysRepository>();
        builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
        builder.Services.AddScoped<IVitalsHistory, EfVitalsHistory>();
        builder.Services.AddHashChain<Wheelhouse.Domain.Audit.Entities.AuditEntry,
            Wheelhouse.Persistence.Audit.AuditEntryCanonicalizer>();

        return builder;
    }

    /// <summary>Registers the infrastructure layer — the SDK time provider, the runner gateway, the inventory snapshot and
    /// its startup export, the vault client, and the OAuth-token source the icon reads use.</summary>
    public static WebApplicationBuilder AddInfrastructureLayer(this WebApplicationBuilder builder)
    {
        builder.Services.AddTimeProviders();
        builder.Services.AddSingleton(DeploymentSettingsFor(builder));
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.Deployments.Parsers.RunnerFailureParser>();
        builder.Services.AddScoped<IDeploymentGateway, Wheelhouse.Infrastructure.Deployments.DeploymentGateway>();
        builder.Services.AddScoped<Wheelhouse.Application.Operations.VitalsSampling>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Operations.VitalsSampler>();

        // The database owns the inventory; the runner reads the snapshot exported at startup and after every change.
        builder.Services.AddScoped<IInventorySnapshot, Wheelhouse.Infrastructure.Inventory.InventorySnapshotExporter>();
        builder.Services.AddScoped<Wheelhouse.Infrastructure.Inventory.LocalRigSeedService>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Inventory.InventoryStartupService>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Deployments.DeploymentOutcomeFollower>();
        builder.Services.AddScoped<ITargetSites, Wheelhouse.Infrastructure.Products.RunnerTargetSites>();

        // Vault administration: inventory endpoints only, no redirects or cookies, bounded calls.
        builder.Services.AddHttpClient(Wheelhouse.Infrastructure.Vaults.VaultGateway.ClientName,
                client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.Vaults.VaultSessionCache>();
        builder.Services.AddScoped<IVaultGateway, Wheelhouse.Infrastructure.Vaults.VaultGateway>();

        // The integration clients read the signed-in admin's OAuth token off the current request.
        builder.Services.AddHttpContextAccessTokenProvider();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient(Wheelhouse.Infrastructure.Products.GitHubProductIconSource.ClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Wheelhouse");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });
        builder.Services.AddScoped<IProductIconSource, Wheelhouse.Infrastructure.Products.GitHubProductIconSource>();

        return builder;
    }

    /// <summary>Registers the application layer — the mediator with its audit, inventory export and validation steps,
    /// the projection services, and the application assembly's handlers and validators.</summary>
    public static WebApplicationBuilder AddApplicationLayer(this WebApplicationBuilder builder)
    {
        builder.Services.AddMediator(typeof(IApplicationMarker).Assembly);
        // Registered before validation so it wraps it: a refused request is audited too.
        builder.Services.AddMediatorInterceptor(typeof(Wheelhouse.Application.Audit.Interceptors.AuditRecordingInterceptor<,>));
        builder.Services.AddMediatorInterceptor(typeof(Wheelhouse.Application.Inventory.Interceptors.InventoryExportingInterceptor<,>));
        builder.Services.AddMediatorValidatingInterceptor();
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductsProjectionService>();
        builder.Services.AddScoped<Wheelhouse.Application.Targets.Services.TargetsProjectionService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IOperatorContext, Wheelhouse.Api.Auth.HttpOperatorContext>();
        builder.Services.AddFluentValidatorsFromAssemblies(typeof(IApplicationMarker).Assembly);

        return builder;
    }

    /// <summary>Registers API services — controllers + enum-as-string JSON via the SDK helper (OpenAPI is wired by <c>AddApiDefaults</c>).</summary>
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddControllers()
            .AddJsonStringEnums();

        return builder;
    }

    // Relative runner paths resolve against the content root, so a local run points at the repository checkout.
    private static Wheelhouse.Infrastructure.Settings.DeploymentSettings DeploymentSettingsFor(WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("Deployment")
            .Get<Wheelhouse.Infrastructure.Settings.DeploymentSettings>() ?? new();
        if (settings.Rehearsal is not (Wheelhouse.Infrastructure.Settings.DeploymentSettings.RigOff
                or Wheelhouse.Infrastructure.Settings.DeploymentSettings.RigHost
                or Wheelhouse.Infrastructure.Settings.DeploymentSettings.RigNetwork))
            throw new InvalidOperationException("Deployment:Rehearsal must be empty, 'host' or 'network'.");

        string Resolve(string path) => path.Length == 0 || Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, path));
        return settings with
        {
            TransportPath = Resolve(settings.TransportPath),
            Root = Resolve(settings.Root),
            GitHubTokenFile = Resolve(settings.GitHubTokenFile),
            RehearsalState = Resolve(settings.RehearsalState)
        };
    }
}
