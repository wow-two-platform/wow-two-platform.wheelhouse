using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Wheelhouse.Application;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Persistence;
using Wheelhouse.Persistence.Repositories;
using WoW.Two.Sdk.Beta.Be.Caching.Hybrid;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Time;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Http.Safety;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Validation;
using WoW.Two.Sdk.Beta.Be.Meta;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.DataProtection;
using WoW.Two.Sdk.Beta.Be.Web.Json;
using WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;

namespace Wheelhouse.Api.Configurations;

/// <summary>Per-concern host registration extensions for the Wheelhouse API.</summary>
public static class HostConfigurationExtensions
{
    /// <summary>Holds the name that isolates Wheelhouse's Data Protection keys; sign-in cookies depend on it.</summary>
    private const string DataProtectionApplicationName = "Wheelhouse";

    /// <summary>Holds the header every operator write carries its action name in; the console sends it.</summary>
    public const string ActionHeaderName = "X-Wheelhouse-Action";

    /// <summary>Loads optional local settings overrides (gitignored) and keeps the cookie key ring on disk.</summary>
    public static WebApplicationBuilder AddSettings(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

        // Sign-in survives restarts and container replacement: keys live in DataProtection:KeyPath (the image sets
        // /data/keys); startup fails outside Development without it.
        var configuration = builder.Configuration;
        builder.Services.AddPersistentDataProtection(o =>
        {
            o.ApplicationName = DataProtectionApplicationName;
            o.KeyDirectory = configuration["DataProtection:KeyPath"];
        });
        return builder;
    }

    /// <summary>Configures the SDK boot floor for a single-user control plane — output cache, rate limiting, and OTLP export off; OpenAPI in development only;
    /// the SPA served inside the pipeline; forwarded headers trusted only from loopback and <c>Deployment:TrustedProxies</c> —
    /// and the opt-in HTTP ProblemDetails boundary, so every error the host writes is a problem document.</summary>
    public static WebApplicationBuilder AddPlatformDefaults(this WebApplicationBuilder builder)
    {
        var proxies = builder.Configuration.GetSection("Deployment:TrustedProxies").Get<string[]>() ?? [];
        builder.AddApiDefaults(o =>
        {
            o.ServiceName = "wheelhouse";
            o.EnableOutputCache = false;
            o.EnableRateLimiting = false;
            o.EnableOtlpExporters = false;
            o.ExposeOpenApi = builder.Environment.IsDevelopment();
            o.SpaHosting = static _ => { };
            foreach (var proxy in proxies)
                o.TrustedProxies.Add(proxy);
        });
        builder.Services.AddHttpProblemDetails();
        return builder;
    }

    /// <summary>Registers the persistence layer — the SDK one-call Postgres bundle (data source, connection factory, audit interceptor, snake_case EF context, embedded bespoke migrator) plus the Wheelhouse stores.</summary>
    public static WebApplicationBuilder AddPersistenceLayer(this WebApplicationBuilder builder)
    {
        // One-call host floor for the context: resolves the connection string, builds the shared NpgsqlDataSource,
        // registers the Dapper connection factory + audit interceptor, adds the snake_case EF context, and wires the
        // embedded bespoke migrator over typeof(WheelhouseDbContext).Assembly. Keep Wheelhouse's existing config key
        // (ConnectionStrings:Wheelhouse) instead of the SDK default (DatabaseSettings:ConnectionString); env DB_CONNECTION still overrides.
        builder.Services.AddPostgresPersistence<WheelhouseDbContext>(
            builder.Configuration,
            o =>
            {
                o.ConnectionStringConfigKey = WheelhouseDatabase.ConnectionStringConfigKey;
                // Applied rows carry the product version they shipped in (vX.Y), not the SDK default v1.0.
                o.ConfigureMigrations = migrations =>
                    migrations.Version = MigrationVersionMapper.ToVersion(typeof(HostConfigurationExtensions).Assembly);
            });

        // /api/system/ready reports this check; /health stays liveness and skips readiness checks.
        builder.Services.AddHealthChecks()
            .AddDatabaseReadinessCheck<WheelhouseDbContext>(tags: [WheelhouseDatabase.ReadinessTag]);
        builder.Services.Configure<HealthCheckOptions>(o =>
            o.Predicate = check => !check.Tags.Contains(WheelhouseDatabase.ReadinessTag));

        builder.Services.AddScoped<IProductsRepository, ProductsRepository>();
        builder.Services.AddScoped<IProductManifestsRepository, EfProductManifests>();
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
    /// its startup export, the vault client, the OAuth-token source and the GitHub repository reader the icon and
    /// manifest reads share.</summary>
    public static WebApplicationBuilder AddInfrastructureLayer(this WebApplicationBuilder builder)
    {
        builder.Services.AddTimeProviders();
        builder.AddDeploymentSettings();
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.Deployments.Parsers.RunnerFailureParser>();
        builder.Services.AddScoped<IDeploymentGateway, Wheelhouse.Infrastructure.Deployments.DeploymentGateway>();
        builder.Services.AddScoped<Wheelhouse.Application.Operations.VitalsSampling>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Operations.VitalsSampler>();

        // The database owns the inventory; the runner reads the snapshot exported at startup and after every change.
        builder.Services.AddScoped<IInventorySnapshot, Wheelhouse.Infrastructure.Inventory.InventorySnapshotExporter>();
        builder.Services.AddScoped<Wheelhouse.Infrastructure.Inventory.LocalRigSeedService>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Inventory.InventoryStartupService>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Deployments.DeploymentOutcomeFollower>();
        // The runner-sites read holds briefly in the SDK cache, in process.
        builder.Services.AddHybridCaching();
        builder.Services.AddScoped<ITargetSites, Wheelhouse.Infrastructure.Products.RunnerTargetSites>();

        // Vault administration: inventory endpoints only, bounded calls, through the SDK outbound guard (no redirects,
        // proxies, cookies or credentials in the address). Vaults live on private addresses, often over plain HTTP.
        builder.Services.AddHttpClient(Wheelhouse.Infrastructure.Vaults.VaultGateway.ClientName,
                client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddSafeOutboundHttp(o =>
            {
                o.AllowPrivateNetworkTargets = true;
                o.RequireHttps = false;
            });
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.Vaults.VaultSessionCache>();
        builder.Services.AddScoped<IVaultGateway, Wheelhouse.Infrastructure.Vaults.VaultGateway>();

        // The integration clients read the signed-in admin's OAuth token off the current request; the repository reads
        // share the SDK cache.
        builder.Services.AddHttpContextAccessTokenProvider();
        builder.Services.AddGitHubIntegration(Wheelhouse.Infrastructure.Products.GitHubRepositoryReader.ConfigureClient);
        builder.Services.AddScoped<Wheelhouse.Infrastructure.Products.GitHubRepositoryReader>();
        builder.Services.AddScoped<IProductIconSource, Wheelhouse.Infrastructure.Products.GitHubProductIconSource>();
        builder.Services.AddScoped<IProductManifestSource, Wheelhouse.Infrastructure.Products.GitHubProductManifestSource>();

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
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductIdentityService>();
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductsProjectionService>();
        builder.Services.AddScoped<Wheelhouse.Application.Targets.Services.TargetsProjectionService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IOperatorContext, Wheelhouse.Api.Auth.HttpOperatorContext>();
        builder.Services.AddFluentValidatorsFromAssemblies(typeof(IApplicationMarker).Assembly);

        return builder;
    }

    /// <summary>Registers API services — controllers + enum-as-string JSON via the SDK helper (OpenAPI is wired by <c>AddApiDefaults</c>),
    /// and the header every operator write names itself in, which the SDK action-header guard reads.</summary>
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddControllers()
            .AddJsonStringEnums();
        builder.Services.AddActionHeaderGuard(o => o.HeaderName = ActionHeaderName);

        return builder;
    }

    /// <summary>Binds the operator-owned runner paths from <c>Deployment</c> when first resolved, checks the rig switch at
    /// boot, and resolves relative paths against the content root, so a local run points at the repository checkout.</summary>
    private static WebApplicationBuilder AddDeploymentSettings(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        builder.Services.AddValidatedOptions<DeploymentSettings>(
            settings => configuration.GetSection("Deployment").Bind(settings),
            options => options.Validate(
                settings => settings.Rehearsal is DeploymentSettings.RigOff or DeploymentSettings.RigHost or DeploymentSettings.RigNetwork,
                "Deployment:Rehearsal must be empty, 'host' or 'network'."));
        builder.Services.AddOptions<DeploymentSettings>().PostConfigure<IHostEnvironment>((settings, environment) =>
        {
            string Resolve(string path) => path.Length == 0 || Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
            settings.TransportPath = Resolve(settings.TransportPath);
            settings.Root = Resolve(settings.Root);
            settings.GitHubTokenFile = Resolve(settings.GitHubTokenFile);
            settings.RehearsalState = Resolve(settings.RehearsalState);
        });
        return builder;
    }
}
