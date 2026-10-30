using System.Text.Json;
using System.Text.RegularExpressions;
using Wheelhouse.Application;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Jobs;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Persistence;
using Wheelhouse.Persistence.Repositories;
using WoW.Two.Sdk.Beta.Be.Caching.Hybrid;
using WoW.Two.Sdk.Beta.Be.Caching.Memo;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Data.Sessions;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Foundation.Time;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Http.Safety;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Mediator.Validation;
using WoW.Two.Sdk.Beta.Be.Meta;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.DataProtection;
using WoW.Two.Sdk.Beta.Be.Web.Json;
using WoW.Two.Sdk.Beta.Be.Web.ProblemDetails;
using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers.Generators;

namespace Wheelhouse.Api.Configurations;

/// <summary>Per-concern host registration extensions for the Wheelhouse API.</summary>
public static partial class HostConfigurationExtensions
{
    /// <summary>Holds the name that isolates Wheelhouse's Data Protection keys; sign-in cookies depend on it.</summary>
    private const string DataProtectionApplicationName = "Wheelhouse";

    /// <summary>Holds the header every owner write carries its action name in; the console sends it.</summary>
    public const string ActionHeaderName = "X-Wheelhouse-Action";

    /// <summary>Loads the settings file a deployed target mounts (<c>appsettings.Target.json</c>) and, on a local run, this
    /// machine's own (<c>appsettings.Local.json</c>, gitignored), and keeps the cookie key ring on disk.</summary>
    public static WebApplicationBuilder AddSettings(this WebApplicationBuilder builder)
    {
        // Before anything reads settings. The local rig's database, runner paths and switch sit in the machine's file,
        // never appsettings.Development.json, which deployed dev reads.
        builder.AddSettingsFiles();

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

    /// <summary>Configures the SDK boot floor for a single-user control plane — output cache, rate limiting, and OTLP export off; OpenAPI on a local run only (the SDK default);
    /// the SPA served inside the pipeline (<c>/.well-known/*</c> 404s, reserved by the SDK); readiness at
    /// <c>/api/system/ready</c>; forwarded headers trusted only from loopback and <c>Deployment:TrustedProxies</c> —
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
            o.SpaHosting = static _ => { };
            // Deployment health gates probe readiness; /health stays liveness and skips the database check.
            o.ReadinessEndpointPath = "/api/system/ready";
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
        // The SDK data session: writes that commit together or not at all, and the after-commit hooks (the inventory
        // export). An export waits its turn behind another and reads the whole inventory, so its hook gets 30 seconds.
        builder.Services.AddDataSession<WheelhouseDbContext>(o => o.HookTimeout = TimeSpan.FromSeconds(30));

        // The SDK readiness endpoint, /api/system/ready, reports this check (tag `ready`); /health skips it.
        builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<WheelhouseDbContext>();

        builder.Services.AddScoped<IWorkspacesRepository, WorkspacesRepository>();
        builder.Services.AddScoped<IPinsRepository, PinsRepository>();
        builder.Services.AddScoped<IProductsRepository, ProductsRepository>();
        builder.Services.AddScoped<IProductManifestsRepository, EfProductManifests>();
        builder.Services.AddScoped<IServiceStatusesRepository, ServiceStatusesRepository>();
        builder.Services.AddScoped<IEnvironmentSettingsRepository, EnvironmentSettingsRepository>();
        builder.Services.AddScoped<IServersRepository, ServersRepository>();
        builder.Services.AddScoped<ITargetsRepository, TargetsRepository>();
        builder.Services.AddScoped<IVaultsRepository, VaultsRepository>();
        builder.Services.AddScoped<IIntegrationKeysRepository, IntegrationKeysRepository>();
        builder.Services.AddScoped<IApiKeyRepository, IntegrationKeysRepository>();
        builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
        builder.Services.AddScoped<IDeploymentAutomationStore, DeploymentAutomationStore>();
        builder.Services.AddScoped<IDeployFreezesRepository, DeployFreezesRepository>();
        builder.Services.AddScoped<IFleetRolloutsRepository, FleetRolloutsRepository>();
        builder.Services.AddScoped<IVitalsHistory, EfVitalsHistory>();
        // The built-in Secrets Vault's tables (schema vault), the keys owners add, renders and scheduled applies.
        builder.Services.AddScoped<ISecretsVaultStore, SecretsVaultStore>();
        builder.Services.AddScoped<IVaultRootKeyVerifications, VaultRootKeyVerificationsRepository>();
        builder.Services.AddScoped<IProductSettingKeysRepository, ProductSettingKeysRepository>();
        builder.Services.AddScoped<IDeploymentRenders, DeploymentRendersRepository>();
        builder.Services.AddScoped<ISettingsApplies, SettingsAppliesRepository>();
        builder.Services.AddScoped<IStandaloneVaultReader, StandaloneVaultDatabaseReader>();
        builder.Services.AddHashChain<Wheelhouse.Domain.Audit.Entities.AuditEntry,
            Wheelhouse.Persistence.Audit.AuditEntryCanonicalizer>();

        return builder;
    }

    /// <summary>Registers the infrastructure layer — the SDK time provider, the runner gateway, the inventory snapshot and
    /// its startup export, the vault client, GitHub access (the App's per-owner tokens, the fallbacks, the access issues),
    /// the GitHub repository reader the icon, identity, deploy.yml and catalog reads share, and the catalog settings.</summary>
    public static WebApplicationBuilder AddInfrastructureLayer(this WebApplicationBuilder builder)
    {
        builder.Services.AddTimeProviders();
        builder.AddDeploymentSettings();
        // The runner (transport.py, infra.py): 110 seconds, the environment allowlist, the JSON failure line.
        builder.Services.AddProcessClient(Wheelhouse.Infrastructure.Deployments.RunnerProcess.ClientName,
            (options, _) => Wheelhouse.Infrastructure.Deployments.RunnerProcess.Configure(options));
        builder.Services.AddScoped<IDeploymentGateway, Wheelhouse.Infrastructure.Deployments.DeploymentGateway>();
        // The history projection: one runner read, held in the SDK cache, serves every read for Deployment:HistorySeconds.
        builder.Services.AddScoped<IDeploymentRecords, Wheelhouse.Infrastructure.Deployments.RunnerDeploymentRecords>();
        // The vitals snapshot: one runner read and one SSH read per host serve every tab for Deployment:VitalsSeconds.
        builder.Services.AddSingleton(services =>
            new CoalescingMemo<AppResult<JsonElement>>(services.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<IServerVitals, Wheelhouse.Infrastructure.Deployments.RunnerServerVitals>();
        builder.Services.AddScoped<Wheelhouse.Application.Operations.VitalsSampling>();
        builder.Services.AddVitalsSamplingJob();

        // The database owns the inventory; the runner reads the snapshot exported at startup and after every change.
        builder.Services.AddScoped<IInventorySnapshot, Wheelhouse.Infrastructure.Inventory.InventorySnapshotExporter>();
        builder.Services.AddScoped<Wheelhouse.Infrastructure.Inventory.LocalRigSeedService>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Inventory.InventoryStartupService>();
        // A failed export keeps the change saved; the Issues panel names it until an export succeeds.
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.Inventory.InventoryExportMonitor>();
        builder.Services.AddSingleton<IInventoryExportIssues>(services =>
            services.GetRequiredService<Wheelhouse.Infrastructure.Inventory.InventoryExportMonitor>());
        // The SDK periodic jobs: deployment observations every Deployment:FollowSeconds; automatic deploys (the release
        // watcher and the per-target queue) every Deployment:AutomationSeconds; the running fleet every Deployment:FleetSeconds.
        builder.Services.AddDeploymentFollowingJob();
        builder.Services.AddDeploymentAutomationJob();
        builder.Services.AddFleetRolloutJob();
        // Infrastructure: Tailscale, enrollment and placement (prod only), and the follower recording finished jobs.
        builder.Services.AddInfraAutomation();
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

        // The built-in Secrets Vault: its root key file (Vault:KeyFile, else {Deployment:Root}/vault/root.key), the SDK's
        // AES-GCM envelope cipher, and the scheduled settings applies every Deployment:ApplySeconds.
        builder.AddSecretsVaultSettings();
        builder.Services.AddEnvelopeCryptography();
        builder.Services.AddSingleton<IVaultKeySource>(services =>
        {
            var vault = services.GetRequiredService<Wheelhouse.Application.SecretsVault.SecretsVaultSettings>();
            var deployment = services.GetRequiredService<DeploymentSettings>();
            return new Wheelhouse.Infrastructure.SecretsVault.FileVaultKeySource(vault.KeyFile.Length > 0
                ? vault.KeyFile
                : Path.Combine(deployment.Root, "vault", "root.key"));
        });
        builder.Services.AddSettingsApplyJob();
        // The root key file's scheduled check (Vault:KeyCheckMinutes), its last result kept for the status and Issues.
        builder.Services.AddVaultKeyCheckJob();

        // GitHub access, per call: the GitHub App's installation token for the owner the call addresses, else the
        // Deployment:GitHubTokenFile token (by default <root>/github-token/token), else the signed-in admin's OAuth token
        // off the current request, else anonymous. The runner gets the same tokens through a file; refusals reach the Issues panel.
        builder.Services.AddHttpClient(Wheelhouse.Infrastructure.GitHub.GitHubAppTokens.ClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Wheelhouse");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.GitHub.GitHubAccessMonitor>();
        builder.Services.AddSingleton<IGitHubAccessIssues>(services =>
            services.GetRequiredService<Wheelhouse.Infrastructure.GitHub.GitHubAccessMonitor>());
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.GitHub.GitHubAppTokens>();
        builder.Services.AddSingleton<Wheelhouse.Infrastructure.GitHub.GitHubCredentials>();
        builder.Services.AddTransient<Wheelhouse.Infrastructure.GitHub.GitHubCredentialHandler>();
        builder.Services.AddScoped<Wheelhouse.Infrastructure.GitHub.GitHubRunnerTokens>();

        // The repository reads share the SDK cache.
        builder.Services.AddHttpContextAccessTokenProvider();
        builder.Services.AddGitHubIntegration(Wheelhouse.Infrastructure.GitHub.GitHubReadSettings.ConfigureClient)
            .AddHttpMessageHandler<Wheelhouse.Infrastructure.GitHub.GitHubCredentialHandler>();
        builder.Services.AddGitHubRepositoryReader(Wheelhouse.Infrastructure.GitHub.GitHubReadSettings.ConfigureReader);
        builder.Services.AddScoped<IProductIconSource, Wheelhouse.Infrastructure.Products.GitHubProductIconSource>();

        // The catalog hubs: one YAML file in each hub repository, bound when first resolved; a malformed setting fails it.
        var configuration = builder.Configuration;
        builder.Services.AddValidatedOptions<Wheelhouse.Application.Catalog.CatalogSettings>(
            settings => configuration.GetSection("Catalog").Bind(settings),
            options => options.Validate(
                settings => settings.IsValid,
                "Catalog:Sources must list at most 20 hubs, each with Repository owner/name (once), Path a relative file "
                + "path and Ref a branch."));

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
        // Generated secret values (SDK cryptographic selection; the SDK registers no generator itself).
        builder.Services.AddSingleton<IIdGenerator, CryptographicIdGenerator>();
        builder.Services.AddScoped<Wheelhouse.Application.Workspaces.Services.DefaultWorkspaceService>();
        builder.Services.AddScoped<Wheelhouse.Application.Catalog.Services.CatalogReader>();
        builder.Services.AddScoped<Wheelhouse.Application.Catalog.Services.CatalogReleaseService>();
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductDescriptorService>();
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductIdentityService>();
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductReleaseSourcesService>();
        builder.Services.AddScoped<Wheelhouse.Application.Products.Services.ProductsProjectionService>();
        builder.Services.AddScoped<Wheelhouse.Application.Targets.Services.TargetsProjectionService>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.History.Services.DeploymentHistoryService>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Automation.Services.DeploymentAutomationService>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Freezes.Services.DeployFreezeService>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Fleets.Services.FleetRolloutService>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Settings.Services.DeploymentSettingsService>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Settings.Services.SettingsApplyService>();
        builder.Services.AddSingleton<Wheelhouse.Application.SecretsVault.Services.SecretsVaultKeyCache>();
        builder.Services.AddSingleton<Wheelhouse.Application.SecretsVault.Services.SecretsVaultCrypto>();
        builder.Services.AddScoped<Wheelhouse.Application.SecretsVault.Services.SecretsVaultService>();
        builder.Services.AddScoped<Wheelhouse.Application.SecretsVault.Services.SecretsVaultImporter>();
        builder.Services.AddSingleton<Wheelhouse.Application.SecretsVault.Services.RootKeyFileMonitor>();
        builder.Services.AddScoped<Wheelhouse.Application.SecretsVault.Services.RootKeyChecks>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Settings.Services.BuiltInEnvironmentSecrets>();
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Settings.Services.ExternalEnvironmentSecrets>();
        // Deploys take secrets from the built-in vault; Vault:Mode external falls back to the standalone vaults for the
        // one release after the merge.
        builder.Services.AddScoped<Wheelhouse.Application.Deployments.Settings.Services.IEnvironmentSecrets>(services =>
            services.GetRequiredService<Wheelhouse.Application.SecretsVault.SecretsVaultSettings>().IsExternal
                ? services.GetRequiredService<Wheelhouse.Application.Deployments.Settings.Services.ExternalEnvironmentSecrets>()
                : services.GetRequiredService<Wheelhouse.Application.Deployments.Settings.Services.BuiltInEnvironmentSecrets>());
        builder.Services.AddSingleton<Wheelhouse.Application.Deployments.Fleets.Services.FleetRolloutGate>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<BackgroundActor>();
        builder.Services.AddScoped<IActorContext, Wheelhouse.Api.Auth.HttpActorContext>();
        builder.Services.AddFluentValidatorsFromAssemblies(typeof(IApplicationMarker).Assembly);

        return builder;
    }

    /// <summary>Registers API services — controllers + enum-as-string JSON via the SDK helper (OpenAPI is wired by <c>AddApiDefaults</c>),
    /// and the header every owner write names itself in, which the SDK action-header guard reads.</summary>
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddControllers()
            .AddJsonStringEnums();
        builder.Services.AddActionHeaderGuard(o => o.HeaderName = ActionHeaderName);

        return builder;
    }

    /// <summary>Binds the owner-configured runner paths from <c>Deployment</c> when first resolved, checks the rig switch at
    /// boot, and resolves relative paths against the content root, so a local run points at the repository checkout.</summary>
    private static WebApplicationBuilder AddDeploymentSettings(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        builder.Services.AddValidatedOptions<DeploymentSettings>(
            settings => configuration.GetSection("Deployment").Bind(settings),
            options => options
                .Validate(
                    settings => settings.Rehearsal is DeploymentSettings.RigOff or DeploymentSettings.RigHost or DeploymentSettings.RigNetwork,
                    "Deployment:Rehearsal must be empty, 'host' or 'network'.")
                .Validate(
                    settings => settings.GitHubAppId.Length == 0 || GitHubAppIdPattern().IsMatch(settings.GitHubAppId),
                    "Deployment:GitHubAppId must be empty, the GitHub App's ID or its client ID."));
        builder.Services.AddOptions<DeploymentSettings>().PostConfigure<IHostEnvironment>((settings, environment) =>
        {
            string Resolve(string path) => path.Length == 0 || Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
            settings.TransportPath = Resolve(settings.TransportPath);
            settings.Root = Resolve(settings.Root);
            settings.GitHubTokenFile = Resolve(settings.GitHubTokenFile);
            settings.GitHubAppId = settings.GitHubAppId.Trim();
            // The key sits beside the runner's other owner-placed credentials unless the host names another path.
            settings.GitHubAppKeyFile = settings.GitHubAppKeyFile.Length == 0
                ? settings.Root.Length == 0 ? "" : Path.Combine(settings.Root, "github-app", "private-key.pem")
                : Resolve(settings.GitHubAppKeyFile);
            settings.RehearsalState = Resolve(settings.RehearsalState);
        });
        return builder;
    }

    /// <summary>Binds the built-in Secrets Vault's settings from <c>Vault</c> when first resolved: <c>Mode</c>
    /// <c>builtin</c> or <c>external</c>, the root key file, resolved against the content root, and how many days a check
    /// of the owner's offline copy counts.</summary>
    private static WebApplicationBuilder AddSecretsVaultSettings(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        builder.Services.AddValidatedOptions<Wheelhouse.Application.SecretsVault.SecretsVaultSettings>(
            settings => configuration.GetSection("Vault").Bind(settings),
            options => options
                .Validate(
                    settings => settings.Mode is Wheelhouse.Application.SecretsVault.Constants.SecretsVaultModes.BuiltIn
                        or Wheelhouse.Application.SecretsVault.Constants.SecretsVaultModes.External,
                    "Vault:Mode must be 'builtin' or 'external'.")
                .Validate(settings => settings.OfflineCopyDays is >= 1 and <= 3650, "Vault:OfflineCopyDays must be 1–3650."));
        builder.Services.AddOptions<Wheelhouse.Application.SecretsVault.SecretsVaultSettings>().PostConfigure<IHostEnvironment>(
            (settings, environment) =>
            {
                settings.Mode = settings.Mode.Trim().ToLowerInvariant();
                if (settings.KeyFile.Length > 0 && !Path.IsPathRooted(settings.KeyFile))
                    settings.KeyFile = Path.GetFullPath(Path.Combine(environment.ContentRootPath, settings.KeyFile));
            });
        return builder;
    }

    /// <summary>Matches a GitHub App ID (digits) or client ID (<c>Iv…</c>).</summary>
    [GeneratedRegex("^[A-Za-z0-9.]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubAppIdPattern();
}
