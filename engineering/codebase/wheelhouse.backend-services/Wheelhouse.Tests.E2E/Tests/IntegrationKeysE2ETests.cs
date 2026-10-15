using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Npgsql;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Backend.Beta.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for integration keys — the operator creates and revokes them; a program presents one to read the catalog and
/// reaches nothing else. The secret leaves the host once, in the creation response.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class IntegrationKeysE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private HttpClient KeyClient(string secret)
    {
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private async Task<IntegrationKeyWithSecretResponse> CreateAsync(string name = "Claude")
    {
        var response = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name, scopes = new[] { "catalog:read" } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.ReadEnvelopeAsync<IntegrationKeyWithSecretResponse>();
    }

    [Fact]
    public async Task Create_ShouldReturn201WithTheSecretOnce_WhenTheOperatorCreatesAKey()
    {
        var response = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "Claude", scopes = new[] { "catalog:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var created = await response.ReadEnvelopeAsync<IntegrationKeyWithSecretResponse>();
        created.Secret.Should().MatchRegex("^wh_[A-Za-z0-9]{32}$");
        created.Key.Prefix.Should().Be(created.Secret[..11]);
        created.Key.Scopes.Should().Equal("catalog:read");
        created.Key.CreatedBy.Should().Be("test-admin");

        var listed = await AdminClient.GetStringAsync("api/integration-keys");
        listed.Should().Contain(created.Key.Prefix).And.NotContain(created.Secret);
    }

    [Fact]
    public async Task Key_ShouldReadTheCatalog_WhenItGrantsCatalogRead()
    {
        var created = await CreateAsync();
        var client = KeyClient(created.Secret);

        (await client.GetAsync("api/products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("api/products/foreverpin")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("api/products/wheelhouse/icon")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var viaHeader = AnonymousClient;
        viaHeader.DefaultRequestHeaders.Add("X-Api-Key", created.Secret);
        (await viaHeader.GetAsync("api/products")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Key_ShouldBeRefused_WhenItReachesBeyondTheCatalog()
    {
        var created = await CreateAsync();
        var client = KeyClient(created.Secret);
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "lifecycle");

        (await client.GetAsync("api/deployments/targets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("api/integration-keys")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutJsonAsync("api/products/foreverpin/lifecycle", new { lifecycle = "live" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Key_ShouldReturn403_WhenItLacksTheCatalogScope()
    {
        // No request creates a scope-less key; one written straight to the store proves the scope, not the key, admits.
        const string secret = "wh_ABCDEFGHIJKLMNOPQRSTUVWXYZ012345";
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "INSERT INTO integration_keys (id, name, prefix, hash, scopes, created_by, created_at_utc, updated_at_utc) " +
                "VALUES (@id, 'test-admin', 'wh_ABCDEFGH', @hash, '', 'test-admin', now(), now())", connection);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("hash", Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret))));
            await insert.ExecuteNonQueryAsync();
        }

        (await KeyClient(secret).GetAsync("api/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoke_ShouldReturn200AndStopTheKey_WhenTheOperatorRevokesIt()
    {
        var created = await CreateAsync();

        var response = await ActionClient("key-revoke").PostAsync($"api/integration-keys/{created.Key.Id}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<IntegrationKeyResponse>()).RevokedAt.Should().NotBeNull();
        (await KeyClient(created.Secret).GetAsync("api/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_ShouldShowWhenAKeyWasLastUsed_WhenAProgramPresentedIt()
    {
        var created = await CreateAsync();
        (await KeyClient(created.Secret).GetAsync("api/products")).StatusCode.Should().Be(HttpStatusCode.OK);

        var keys = await (await AdminClient.GetAsync("api/integration-keys")).ReadEnvelopeAsync<IReadOnlyList<IntegrationKeyResponse>>();

        keys.Should().ContainSingle().Which.LastUsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ShouldReturn409_WhenALiveKeyHasTheName()
    {
        await CreateAsync("Codex");

        var response = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "codex", scopes = new[] { "catalog:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PostJsonAsync("api/integration-keys", new { name = "Claude", scopes = new[] { "catalog:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ShouldReturn400_WhenAScopeIsNotGranted()
    {
        var response = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "Claude", scopes = new[] { "deployments:write" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Status.Should().Be(400);
    }

    [Fact]
    public async Task Revoke_ShouldReturn404_WhenNoKeyHasTheId()
    {
        var response = await ActionClient("key-revoke").PostAsync($"api/integration-keys/{Guid.NewGuid()}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
