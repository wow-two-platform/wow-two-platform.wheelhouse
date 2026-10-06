using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using WoW.Two.Sdk.Backend.Beta.Testing;
using WoW.Two.Sdk.Backend.Beta.Testing.Containers.Postgres;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>
/// Owns the single in-process Wheelhouse.Api host the whole E2E run drives, backed by an ephemeral Postgres
/// container. The container + between-test reset are owned by the SDK <see cref="PostgresFixture"/> (Testcontainers
/// + Respawn); this fixture stays the only Wheelhouse-specific piece — it knows the connection-string key, the
/// test-auth + runner/vault/icon stub wiring, and composes the host on top of the SDK <see cref="WebApiTestHost{TEntryPoint}"/>.
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

    /// <summary>The runner root the host exports its inventory snapshot to; a temporary folder per run.</summary>
    public string InventoryRoot { get; } = Directory.CreateTempSubdirectory("wheelhouse-inventory-").FullName;

    /// <summary>The shared runner stub — answers the deployment reads and records what was submitted.</summary>
    public StubDeploymentGateway Deployments { get; } = new();

    /// <summary>The shared vault stub — records the last change so tests can assert what Wheelhouse forwarded.</summary>
    public StubVaultGateway Vaults { get; } = new();

    /// <summary>The shared icon stub — add a repository's icon to <see cref="StubProductIconSource.Icons"/> to serve it.</summary>
    public StubProductIconSource ProductIcons { get; } = new();

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

        // AddPostgresPersistence resolves the connection string eagerly at service registration, where the host hook's
        // config layer isn't applied yet — so use the DB_CONNECTION env seam (it wins over config and is visible at
        // registration time) to point the SDK persistence bundle + bespoke migrator at the container DB. The in-memory
        // config below (the app's own ConnectionStrings:Wheelhouse key) is the belt-and-suspenders for any later config read.
        Environment.SetEnvironmentVariable("DB_CONNECTION", _postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Identity__AllowedGitHubLogins__0", "test-admin");
        // Deployment settings bind at registration, before the host hook's configuration exists.
        Environment.SetEnvironmentVariable("Deployment__Root", InventoryRoot);

        _host = new WebApiTestHost<Program>
        {
            // The SDK host has no connection-string knob — inject it the way the app reads it (ConnectionStrings:Wheelhouse).
            ConfigureHostHook = builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Wheelhouse"] = _postgres.ConnectionString,
                    // Tests run sampling passes explicitly; the background sampler stays off.
                    ["Operations:VitalsSampleMinutes"] = "0",
                    ["Deployment:FollowSeconds"] = "0",
                })),
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

        // Don't leak the container connection string into other test processes.
        Environment.SetEnvironmentVariable("DB_CONNECTION", null);
        Environment.SetEnvironmentVariable("Identity__AllowedGitHubLogins__0", null);
        Environment.SetEnvironmentVariable("Deployment__Root", null);
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
        ProductIcons.Icons.Clear();
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
