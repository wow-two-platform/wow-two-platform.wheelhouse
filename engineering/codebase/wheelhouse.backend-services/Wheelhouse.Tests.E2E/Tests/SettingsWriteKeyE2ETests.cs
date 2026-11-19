using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the write-only <c>settings:write</c> integration key the <c>wow2</c> CLI presents: it saves a target's
/// settings and secrets as the owner's console does, audited as <c>key:{name}</c> by key and version, and reads nothing
/// back: no settings plan, no secret history, no vault, no catalog. A key without the scope is refused the save.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class SettingsWriteKeyE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Secret = "sk-ant-WRITE_ONLY_4c1e";
    private const string Path = "/api/deployments/targets/foreverpin-dev/settings";

    [Fact]
    public async Task AKeyGrantingSettingsWrite_ShouldSaveASecret_AuditedAsTheKey_WithoutItsValue()
    {
        var client = await KeyClientAsync("wow2-cli", "settings:write");

        var response = await client.PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret, secret: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var reply = await response.Content.ReadAsStringAsync();
        reply.Should().NotContain(Secret);
        var written = JsonDocument.Parse(reply).RootElement.GetProperty("data").GetProperty("keys").EnumerateArray().Single();
        (written.GetProperty("key").GetString(), written.GetProperty("secret").GetBoolean(), written.GetProperty("version").GetInt32())
            .Should().Be(("Ai:Providers:anthropic:ApiKey", true, 1));

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "settings.save");
        (audit.GetProperty("actor").GetString(), audit.GetProperty("subject").GetString(), audit.GetProperty("detail").GetString())
            .Should().Be(("key:wow2-cli", "foreverpin-dev", "Ai:Providers:anthropic:ApiKey secret v1"));
        (await AdminClient.GetStringAsync("/api/audit")).Should().NotContain(Secret);

        var plan = await AdminClient.GetStringAsync(Path);
        plan.Should().Contain("Ai:Providers:anthropic:ApiKey").And.NotContain(Secret);
        Fixture.Vaults.Changes.Should().BeEmpty("the built-in vault keeps the environment's secrets");
    }

    [Fact]
    public async Task AKeyGrantingSettingsWrite_ShouldSaveAPlainSetting_AndASecondVersion()
    {
        var client = await KeyClientAsync("wow2-cli", "settings:write");

        (await client.PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret, secret: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await client.PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret + "-2", secret: true));
        var plain = await client.PutAsJsonAsync(Path, Body("Ai:Tagging:Enabled", "true", secret: false));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("keys")[0].GetProperty("version").GetInt32()
            .Should().Be(2);
        plain.StatusCode.Should().Be(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());
        (await plain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("settings").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AKeyGrantingSettingsWrite_ShouldReadNothingBack()
    {
        var client = await KeyClientAsync("wow2-cli", "settings:write");
        (await client.PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret, secret: true))).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var path in new[] { Path, Path + "?release=foreverpin-gh-1", "/api/vault", "/api/audit",
                     "/api/deployments/targets/foreverpin-dev/secrets/Ai:Providers:anthropic:ApiKey/versions",
                     "/api/deployments/targets", "/api/integration-keys" })
        {
            var read = await client.GetAsync(path);
            read.StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], path);
            (await read.Content.ReadAsStringAsync()).Should().NotContain(Secret, path);
        }
        (await client.GetAsync("/api/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the catalog needs catalog:read");
        (await client.PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a settings key never deploys");
    }

    [Fact]
    public async Task AKeyWithoutSettingsWrite_ShouldBeRefusedTheSave()
    {
        var client = await KeyClientAsync("Reader", "catalog:read", "deployments:read");

        var response = await client.PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret, secret: true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("action").GetString() == "settings.save");
    }

    [Fact]
    public async Task AKeyGrantingSettingsWrite_ShouldStillCarryTheActionHeader()
    {
        var client = await KeyClientAsync("wow2-cli", "settings:write");
        client.DefaultRequestHeaders.Remove("X-Wheelhouse-Action");

        (await client.PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret, secret: true)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ARevokedSettingsKey_ShouldBeChallenged()
    {
        var created = await CreateAsync("wow2-cli", "settings:write");
        var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-revoke");
        (await admin.PostAsync($"api/integration-keys/{created.Key.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await KeyClient(created.Secret).PutAsJsonAsync(Path, Body("Ai:Providers:anthropic:ApiKey", Secret, secret: true)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static object Body(string key, string value, bool secret) => new { values = new[] { new { key, value, secret } } };

    private async Task<IntegrationKeyWithSecretResponse> CreateAsync(string name, params string[] scopes)
    {
        var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var response = await admin.PostJsonAsync("api/integration-keys", new { name, scopes });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadEnvelopeAsync<IntegrationKeyWithSecretResponse>();
    }

    private async Task<HttpClient> KeyClientAsync(string name, params string[] scopes) =>
        KeyClient((await CreateAsync(name, scopes)).Secret);

    private HttpClient KeyClient(string secret)
    {
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "settings");
        return client;
    }
}
