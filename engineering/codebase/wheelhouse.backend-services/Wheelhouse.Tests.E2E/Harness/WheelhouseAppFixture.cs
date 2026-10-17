using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using WoW.Two.Sdk.Beta.Be.Testing;
using WoW.Two.Sdk.Beta.Be.Testing.Containers.Postgres;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>
/// Owns the single in-process Wheelhouse.Api host the whole E2E run drives, backed by an ephemeral Postgres
/// container. The container + between-test reset are owned by the SDK <see cref="PostgresFixture"/> (Testcontainers
/// + Respawn); this fixture stays the only Wheelhouse-specific piece — it knows the connection-string key, the
/// test-auth + runner/vault/icon/manifest stub wiring, and composes the host on top of the SDK <see cref="WebApiTestHost{TEntryPoint}"/>.
/// </summary>
/// <remarks>
/// Lifecycle: start the Postgres fixture → build the host (its <c>InitializeAsync()</c> runs the bespoke SQL migrator,
/// so the schema exists) → snapshot the post-migration schema once via <see cref="PostgresFixture.InitializeRespawnerAsync"/>.
/// <see cref="ResetAsync"/> then truncates data (Respawn ignores <c>migration_history</c>, so migrations never re-run).
/// </remarks>
public sealed class WheelhouseAppFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new(new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build());

    private WebApiTestHost<Program>? _host;
    private bool _respawnerReady;

    /// <summary>The booted Api host.</summary>
    public WebApiTestHost<Program> Host =>
        _host ?? throw new InvalidOperationException("Fixture not initialized — Host is null.");

    /// <summary>The connection string of the run's Postgres container, the only database the host may use.</summary>
    public string ConnectionString => _postgres.ConnectionString;

    /// <summary>The runner root the host exports its inventory snapshot to; a temporary folder per run.</summary>
    public string InventoryRoot { get; } = Directory.CreateTempSubdirectory("wheelhouse-inventory-").FullName;

    /// <summary>The browser origin configured in <c>Mcp:AllowedOrigins</c>.</summary>
    public const string McpAllowedOrigin = "http://localhost:6274";

    /// <summary>The shared runner stub — answers the deployment reads and records what was submitted.</summary>
    public StubDeploymentGateway Deployments { get; } = new();

    /// <summary>The shared vault stub — records the last change so tests can assert what Wheelhouse forwarded.</summary>
    public StubVaultGateway Vaults { get; } = new();

    /// <summary>The shared icon stub — add files with <see cref="StubProductIconSource.Add"/> to serve a repository's icons.</summary>
    public StubProductIconSource ProductIcons { get; } = new();

    /// <summary>The shared manifest stub — push a commit with <see cref="StubProductManifestSource.Push(string, string, string)"/> to give a repository its manifest.</summary>
    public StubProductManifestSource ProductManifests { get; } = new();

    /// <summary>A fresh anonymous client (no admin header) — protected endpoints return 401.</summary>
    public HttpClient CreateAnonymousClient() => Host.CreateClient();

    /// <summary>A fresh client authenticated as a valid admin (carries the test-admin header).</summary>
    public HttpClient CreateAdminClient()
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthExtensions.AdminHeader, "1");
        return client;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // The identity allowlist binds at registration, before the host hook's configuration exists.
        Environment.SetEnvironmentVariable("Identity__AllowedGitHubLogins__0", "test-admin");

        _host = new WebApiTestHost<Program>
        {
            // Host-local configuration, read lazily: the SDK persistence bundle resolves its connection string, and the
            // deployment settings bind, when first resolved. DB_CONNECTION is the key the bundle lets override the
            // app's own; pinning both to the container keeps a stray DB_CONNECTION in the shell off the dev database.
            ConfigureConfigurationHook = config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Wheelhouse"] = _postgres.ConnectionString,
                ["DB_CONNECTION"] = _postgres.ConnectionString,
                ["Deployment:Root"] = InventoryRoot,
                // Tests run sampling passes explicitly; the background sampler stays off.
                ["Operations:VitalsSampleMinutes"] = "0",
                ["Deployment:FollowSeconds"] = "0",
                // A browser client the operator allowed, beside the console's own origin.
                ["Mcp:AllowedOrigins:0"] = McpAllowedOrigin,
            }),
            ConfigureServicesHook = services =>
            {
                services.UseTestAdminAuth();
                services.RemoveAll<Wheelhouse.Application.Abstractions.IDeploymentGateway>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IDeploymentGateway>(Deployments);
                services.RemoveAll<Wheelhouse.Application.Abstractions.IVaultGateway>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IVaultGateway>(Vaults);

                // Product icons come from the stub, never from GitHub.
                services.RemoveAll<Wheelhouse.Application.Abstractions.IProductIconSource>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IProductIconSource>(ProductIcons);

                // Product manifests come from the stub too; a repository nobody pushed to has none and cannot be read.
                services.RemoveAll<Wheelhouse.Application.Abstractions.IProductManifestSource>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IProductManifestSource>(ProductManifests);
            },
        };

        // Force the host to build and run its startup migration (the bespoke migrator creates the schema).
        _ = Host.Services;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();

        await _postgres.DisposeAsync();

        // Don't leak the allowlist into other test processes.
        Environment.SetEnvironmentVariable("Identity__AllowedGitHubLogins__0", null);
        Directory.Delete(InventoryRoot, recursive: true);
    }

    /// <summary>Truncates every data table between tests via Respawn (the migration history is preserved), and resets the stubs.</summary>
    public async Task ResetAsync()
    {
        // Snapshot the schema once, after the host has migrated — the respawner reflects the real post-migration shape.
        if (!_respawnerReady)
        {
            await _postgres.InitializeRespawnerAsync();
            _respawnerReady = true;
        }

        await _postgres.ResetAsync();
        ResetStubs();
    }

    /// <summary>Restores the shared stubs to their happy-path defaults so each test starts from a known state.</summary>
    private void ResetStubs()
    {
        Deployments.StartRefusal = null;
        Deployments.ReadFailure = null;
        Deployments.ReadException = null;
        ProductIcons.Clear();
        ProductManifests.Clear();
    }
}

/// <summary>xUnit collection sharing one <see cref="WheelhouseAppFixture"/> across the whole E2E run.</summary>
[CollectionDefinition(WheelhouseCollection.Name)]
public sealed class WheelhouseCollection : ICollectionFixture<WheelhouseAppFixture>
{
    /// <summary>Collection name — every E2E class joins this so they share the host and run serially.</summary>
    public const string Name = "wheelhouse-e2e";
}

/// <summary>
/// Convenience base for E2E tests — wires the shared fixture, resets the DB before each test, and exposes
/// fresh clients. Concrete test classes carry <c>[Collection(WheelhouseCollection.Name)]</c>.
/// </summary>
public abstract class WheelhouseE2EBase(WheelhouseAppFixture fixture) : IAsyncLifetime
{
    /// <summary>The shared app fixture (host + Postgres DB).</summary>
    protected WheelhouseAppFixture Fixture { get; } = fixture;

    /// <summary>An anonymous client (no admin header).</summary>
    protected HttpClient AnonymousClient => Fixture.CreateAnonymousClient();

    /// <summary>A client authenticated as a valid admin.</summary>
    protected HttpClient AdminClient => Fixture.CreateAdminClient();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        await InventorySeed.SeedAsync(Fixture);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;
}
