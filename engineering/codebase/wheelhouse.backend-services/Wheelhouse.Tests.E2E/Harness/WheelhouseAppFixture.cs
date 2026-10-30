using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using WoW.Two.Sdk.Beta.Be.Caching.Memo;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Testing;
using WoW.Two.Sdk.Beta.Be.Testing.Containers.Postgres;
using WoW.Two.Sdk.Beta.Be.Testing.Integrations;

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
    // The PostgreSQL major every wow2 host and test runs (postgres convention § Version).
    private readonly PostgresFixture _postgres = new(new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build());

    private WebApiTestHost<Program>? _host;
    private bool _respawnerReady;

    /// <summary>The booted Api host.</summary>
    public WebApiTestHost<Program> Host =>
        _host ?? throw new InvalidOperationException("Fixture not initialized — Host is null.");

    /// <summary>The connection string of the run's Postgres container, the only database the host may use.</summary>
    public string ConnectionString => _postgres.ConnectionString;

    /// <summary>The runner root the host exports its inventory snapshot to; a temporary folder per run.</summary>
    public string InventoryRoot { get; } = Directory.CreateTempSubdirectory("wheelhouse-inventory-").FullName;

    /// <summary>The built-in vault's root key file, a fresh random key per run; tests may swap or remove it to seal the
    /// vault, and each reset puts it back.</summary>
    public string VaultKeyFile { get; } = Path.Combine(Directory.CreateTempSubdirectory("wheelhouse-vault-").FullName, "root.key");

    /// <summary>The root key the run's vault was unsealed with, as the key file holds it.</summary>
    public string VaultKey { get; } = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>The browser origin configured in <c>Mcp:AllowedOrigins</c>.</summary>
    public const string McpAllowedOrigin = "http://localhost:6274";

    /// <summary>The hostname this console answers on, as a Wheelhouse deploy renders its own site's URL
    /// (<c>Sites:console:Url</c>); Wheelhouse's own environments take it as their console site.</summary>
    public const string ConsoleHost = "wheelhouse.tail1234.ts.net";

    /// <summary>The shared runner stub — answers the deployment reads and records what was submitted.</summary>
    public StubDeploymentGateway Deployments { get; } = new();

    /// <summary>The shared vault stub — records the last change so tests can assert what Wheelhouse forwarded.</summary>
    public StubVaultGateway Vaults { get; } = new();

    /// <summary>The shared infrastructure stub — records Tailscale, enrollment and placement requests.</summary>
    public StubInfraGateway Infra { get; } = new();

    /// <summary>The shared icon stub — add files with <see cref="StubProductIconSource.Add"/> to serve a repository's icons.</summary>
    public StubProductIconSource ProductIcons { get; } = new();

    /// <summary>The shared SDK GitHub fake under the real repository reader — push files with
    /// <see cref="FakeGitHubClient.Push(string, string, string, string, string)"/> to give the catalog repository its catalog
    /// and a product repository its <c>deploy.yml</c> (with its identity) or a retired <c>product/product.json</c>. A
    /// repository nobody pushed to cannot be read.</summary>
    public FakeGitHubClient GitHub { get; } = new FakeGitHubClient().WithPushedOnly();

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
        await File.WriteAllTextAsync(VaultKeyFile, VaultKey + "\n");

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
                // Tests run automation passes explicitly too, and move fleet rollouts on themselves.
                ["Deployment:AutomationSeconds"] = "0",
                ["Deployment:FleetSeconds"] = "0",
                // Tests record finished infrastructure jobs explicitly.
                ["Infra:FollowSeconds"] = "0",
                // Every history read asks the stub, so a test sees the records it just wrote.
                ["Deployment:HistorySeconds"] = "0",
                // A browser client the owner allowed, beside the console's own origin.
                ["Mcp:AllowedOrigins:0"] = McpAllowedOrigin,
                // The built-in vault reads the run's own root key; tests run scheduled settings applies themselves.
                ["Vault:KeyFile"] = VaultKeyFile,
                ["Deployment:ApplySeconds"] = "0",
                // The dev console's own site, as its render names it: the hostname, on dev's port.
                ["Sites:console:Url"] = "https://" + ConsoleHost + ":8443",
                // The reader keeps what it read per commit; it reads the head on every call, so each push shows at once.
                ["Integrations:GitHub:Reader:HeadFor"] = "00:00:00",
            }),
            ConfigureServicesHook = services =>
            {
                services.UseTestAdminAuth();
                services.RemoveAll<Wheelhouse.Application.Abstractions.IDeploymentGateway>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IDeploymentGateway>(Deployments);
                services.RemoveAll<Wheelhouse.Application.Abstractions.IVaultGateway>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IVaultGateway>(Vaults);
                services.RemoveAll<Wheelhouse.Application.Abstractions.IInfraGateway>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IInfraGateway>(Infra);

                // Product icons come from the stub, never from GitHub.
                services.RemoveAll<Wheelhouse.Application.Abstractions.IProductIconSource>();
                services.AddSingleton<Wheelhouse.Application.Abstractions.IProductIconSource>(ProductIcons);

                // The catalog, deploy.yml and identity reads go through the real reader to the GitHub fake.
                services.RemoveAll<IGitHubClient>();
                services.AddSingleton<IGitHubClient>(GitHub);
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
        Directory.Delete(Path.GetDirectoryName(VaultKeyFile)!, recursive: true);
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
        // The vault schema sits outside Respawn's public schema: empty it too, and put the run's root key back.
        await using (var connection = new Npgsql.NpgsqlConnection(_postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var truncate = new Npgsql.NpgsqlCommand(
                "TRUNCATE vault.vault_secret_versions, vault.vault_secrets, vault.vault_product_keys, vault.vault_canary CASCADE",
                connection);
            await truncate.ExecuteNonQueryAsync();
        }
        await File.WriteAllTextAsync(VaultKeyFile, VaultKey + "\n");
        ResetStubs();
        // The vitals snapshot is held for every reader; each test starts without one.
        Host.Services.GetRequiredService<CoalescingMemo<AppResult<JsonElement>>>().Forget();
    }

    /// <summary>Restores the shared stubs to their happy-path defaults so each test starts from a known state.</summary>
    private void ResetStubs()
    {
        Deployments.StartRefusal = null;
        Deployments.RefuseTarget = null;
        Deployments.Runner = null;
        Deployments.ReadFailure = null;
        Deployments.ReadException = null;
        Deployments.History.Clear();
        Deployments.Reads.Clear();
        Deployments.FollowOpen = 0;
        Deployments.FollowNextIn = null;
        Deployments.Latest.Clear();
        Deployments.Builds.Clear();
        Deployments.Starts.Clear();
        Deployments.Controls.Clear();
        Deployments.ControlRefusal = null;
        Deployments.Observations.Clear();
        Deployments.Declarations = null;
        Vaults.Reset();
        Infra.Clear();
        ProductIcons.Clear();
        GitHub.Clear();
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
