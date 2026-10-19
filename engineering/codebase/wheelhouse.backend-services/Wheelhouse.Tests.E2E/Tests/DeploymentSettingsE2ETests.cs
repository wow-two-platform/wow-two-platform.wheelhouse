using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Vaults.Changes;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the settings a deploy renders: the plan the deploy dialog reads (where each key comes from, what is missing,
/// whether it is the first deploy), saving values (secrets to the environment's vault namespace, settings to the store,
/// audited by key only), a deploy refused while a required key has no source, the values a deploy sends the runner,
/// and the keys a readiness check weighs. The runner's declarations come from the stub; its render is covered by the
/// runner's own tests.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class DeploymentSettingsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Secret = "VAULT_ONLY_7f3a";

    [Fact]
    public async Task Settings_ShouldRequireTheOwner_AndTheActionHeaderToSave()
    {
        (await AnonymousClient.GetAsync("/api/deployments/targets/foreverpin-dev/settings?release=v1"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient.PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings", Body(("A:B", "x", false))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a save carries its action header");
        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/settings"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the plan is for one release");
        (await AdminClient.GetAsync("/api/deployments/targets/nothing-here/settings?release=v1"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Plan_ShouldNameEachKeysSource_AndCountWhatBlocksADeploy()
    {
        Declare(hostFile: false);
        Fixture.Vaults.Values["foreverpin-dev"] = new Dictionary<string, string> { ["ImageWorker:Key"] = Secret };
        (await Save(("DataProtection:KeysPath", "/var/lib/keys", false))).StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1");
        (plan.GetProperty("product").GetString(), plan.GetProperty("environment").GetString(), plan.GetProperty("release").GetString(),
                plan.GetProperty("firstDeploy").GetBoolean())
            .Should().Be(("foreverpin", "dev", "v1.0.0", true));
        (plan.GetProperty("vault").GetProperty("id").GetString(), plan.GetProperty("vault").GetProperty("namespace").GetString(),
                plan.GetProperty("vault").GetProperty("reachable").GetBoolean())
            .Should().Be(("pilot-vault", "foreverpin-dev", true));
        var keys = Keys(plan, "desk");
        keys["AllowedHosts"].Should().Be(("derived", "desk.example;ocharo-platform-dev-desk;localhost"));
        keys["ConnectionStrings:Database"].Should().Be(("derived", null), "the target's own database fills it");
        keys["DataProtection:KeysPath"].Should().Be(("stored", "/var/lib/keys"));
        keys["ImageWorker:Key"].Should().Be(("vault", null), "a secret's value is never shown");
        keys["DataProtection:ApplicationName"].Should().Be(("missing", null));
        keys["Feature:Flag"].Should().Be(("default", "false"));
        plan.GetProperty("missing").GetInt32().Should().Be(1);
        (await AdminClient.GetStringAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"))
            .Should().NotContain(Secret);

        // Once a deploy succeeded there, it is no longer the first.
        Fixture.Deployments.History.Add(new
        {
            id = Guid.NewGuid().ToString(), targetId = "foreverpin-dev", environment = "dev", bundleId = "foreverpin-gh-1",
            release = "v1.0.0", actor = "test-admin", status = "succeeded", submittedAt = "2026-10-19T10:00:00+00:00",
        });
        (await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"))
            .GetProperty("firstDeploy").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Save_ShouldSendSecretsToTheVault_StoreSettings_AndAuditKeysOnly()
    {
        Declare(hostFile: false);
        var response = await Save(("DataProtection:ApplicationName", "ocharo-desk", false), ("ImageWorker:Key", Secret, true));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (saved.GetProperty("settings").GetInt32(), saved.GetProperty("secrets").GetInt32()).Should().Be((1, 1));

        var set = Fixture.Vaults.Changes.Select(change => change.Change).OfType<SecretSetChange>().Should().ContainSingle().Subject;
        (set.Namespace, set.Key, set.Value).Should().Be(("foreverpin-dev", "ImageWorker:Key", Secret));
        var keys = Keys(await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"), "desk");
        keys["DataProtection:ApplicationName"].Should().Be(("stored", "ocharo-desk"));
        keys["ImageWorker:Key"].Should().Be(("vault", null));

        var audit = (await DataAsync("/api/audit")).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "settings.save");
        (audit.GetProperty("subject").GetString(), audit.GetProperty("detail").GetString())
            .Should().Be(("foreverpin-dev", "1 setting (DataProtection:ApplicationName); 1 secret (ImageWorker:Key)"));
        (await AdminClient.GetStringAsync("/api/audit")).Should().NotContain(Secret);
    }

    [Theory]
    [InlineData("AllowedHosts", "x", false)]
    [InlineData("Deployment:TrustedProxies", "x", false)]
    [InlineData("Sites:app:Url", "https://x.example", false)]
    [InlineData("Bad Key", "x", false)]
    [InlineData("Logging:Level", "two\nlines", false)]
    [InlineData("Logging:Level", "", false)]
    public async Task Save_ShouldRefuseDerivedKeysAndValuesTheRunnerWouldNot(string key, string value, bool secret) =>
        (await Save((key, value, secret))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Save_ShouldRefuseAKeyGivenTwice() =>
        (await Save(("A:B", "x", false), ("A:B", "y", true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Deploy_ShouldWaitForEveryRequiredKey_AndKeepTheRefusal()
    {
        Declare(hostFile: false);
        var response = await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(
            "Fill in the required settings first: desk: DataProtection:ApplicationName, DataProtection:KeysPath, ImageWorker:Key.");
        Fixture.Deployments.Starts.Should().BeEmpty("the runner never hears of it");
        var refusal = (await DataAsync("/api/deployments/history?notApplied=true")).GetProperty("items").EnumerateArray()
            .Should().ContainSingle().Subject;
        refusal.GetProperty("cause").GetProperty("message").GetString().Should().StartWith("Fill in the required settings first");

        (await Save(("DataProtection:ApplicationName", "ocharo-desk", false), ("DataProtection:KeysPath", "/keys", false)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "the secret is still missing");
        (await Save(("ImageWorker:Key", Secret, true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("ImageWorker:Key").GetString().Should().Be(Secret);
    }

    [Fact]
    public async Task Deploy_ShouldLeaveAHostFilesKeysToTheTarget()
    {
        Declare(hostFile: true);
        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1");
        Keys(plan, "desk")["DataProtection:ApplicationName"].Should().Be(("host", null));
        plan.GetProperty("missing").GetInt32().Should().Be(0);
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Deploy_ShouldSendStoredSettingsAndVaultSecrets_ToTheRunnerOnly()
    {
        Fixture.Vaults.Values["foreverpin-dev"] = new Dictionary<string, string> { ["ImageWorker:Key"] = Secret };
        await Save(("DataProtection:ApplicationName", "ocharo-desk", false), ("ImageWorker:Key", "overridden-by-vault", false));

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var values = Fixture.Deployments.LastSettings!.Value.GetProperty("values");
        values.GetProperty("DataProtection:ApplicationName").GetString().Should().Be("ocharo-desk");
        values.GetProperty("ImageWorker:Key").GetString().Should().Be(Secret, "a vault secret wins over a stored setting");
        var read = Fixture.Vaults.Reads.Should().ContainSingle().Subject;
        (read.Vault, read.Namespace, read.Purpose).Should().Be(("pilot-vault", "foreverpin-dev", "deploy foreverpin-dev"));
        (await AdminClient.GetStringAsync("/api/deployments/history?notApplied=true")).Should().NotContain(Secret);
        (await AdminClient.GetStringAsync("/api/audit")).Should().NotContain(Secret);
    }

    [Fact]
    public async Task Deploy_ShouldWaitForAnUnreachableVault_RatherThanDropASecret()
    {
        Fixture.Vaults.Unreachable = true;
        var response = await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" });
        response.StatusCode.Should().NotBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).Should().Contain("vault pilot-vault is unreachable");
        Fixture.Deployments.Starts.Should().BeEmpty();
        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1");
        plan.GetProperty("vault").GetProperty("reachable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AnEnvironmentWhoseServerHasNoVault_ShouldKeepItsSecretsInTheInventorysVault()
    {
        Declare(hostFile: false);
        (await Save(("ImageWorker:Key", Secret, true), ("DataProtection:ApplicationName", "x", false), target: "foreverpin-prod"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var set = Fixture.Vaults.Changes.Select(change => change).Last();
        (set.Vault, set.Change.Namespace).Should().Be(("pilot-vault", "foreverpin-prod"));
    }

    [Fact]
    public async Task Check_ShouldWeighTheKeysWheelhouseWouldSend()
    {
        Fixture.Vaults.Values["foreverpin-dev"] = new Dictionary<string, string> { ["ImageWorker:Key"] = Secret };
        await Save(("DataProtection:ApplicationName", "ocharo-desk", false));
        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/check?release=foreverpin-gh-1"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastCheckKeys.Should().Equal("DataProtection:ApplicationName", "ImageWorker:Key");
    }

    /// <summary>Makes the runner declare one service, <c>desk</c>, the way it reads a release on a target.</summary>
    private void Declare(bool hostFile) =>
        Fixture.Deployments.Declarations = new
        {
            targetId = "foreverpin-dev", release = "v1.0.0", product = "foreverpin", environment = "dev",
            services = new[]
            {
                new
                {
                    name = "desk", hostFile,
                    keys = new object[]
                    {
                        new { key = "AllowedHosts", required = true, secret = false, derived = "hosts", value = "desk.example;ocharo-platform-dev-desk;localhost" },
                        new { key = "ConnectionStrings:Database", required = true, secret = true, derived = "database" },
                        new { key = "DataProtection:ApplicationName", required = true, secret = false },
                        new { key = "DataProtection:KeysPath", required = true, secret = false },
                        new { key = "ImageWorker:Key", required = true, secret = true },
                        new { key = "Feature:Flag", required = false, secret = false, @default = "false" },
                    },
                },
            },
        };

    private static Dictionary<string, (string?, string?)> Keys(JsonElement plan, string service) =>
        plan.GetProperty("services").EnumerateArray().Single(item => item.GetProperty("name").GetString() == service)
            .GetProperty("keys").EnumerateArray()
            .ToDictionary(key => key.GetProperty("key").GetString()!,
                key => (key.GetProperty("source").GetString(),
                    key.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null));

    private static object Body(params (string Key, string Value, bool Secret)[] values) =>
        new { values = values.Select(value => new { key = value.Key, value = value.Value, secret = value.Secret }) };

    private Task<HttpResponseMessage> Save(params (string Key, string Value, bool Secret)[] values) =>
        Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings", Body(values));

    private Task<HttpResponseMessage> Save((string, string, bool) first, (string, string, bool) second, string target) =>
        Owner("settings").PutAsJsonAsync("/api/deployments/targets/" + target + "/settings", Body(first, second));

    private HttpClient Owner(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
