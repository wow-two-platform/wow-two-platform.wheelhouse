using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the settings a deploy renders: the plan the deploy dialog and the editors read (where each key comes from,
/// what is missing, whether it is the first deploy), saving values (secrets as built-in vault versions, settings to the
/// store, in one transaction, audited by key and version only), typed values checked on save, a secret-looking key
/// refused as a plain setting, a deploy refused while a required key has no source, the values a deploy sends the
/// runner, and the keys a readiness check weighs. The runner's declarations come from the stub; its render is covered
/// by the runner's own tests.
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
        (await AdminClient.GetAsync("/api/deployments/targets/nothing-here/settings?release=v1"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Plan_ShouldNameEachKeysSource_AndCountWhatBlocksADeploy()
    {
        Declare(hostFile: false);
        (await Save(("ImageWorker:Key", Secret, true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save(("DataProtection:KeysPath", "/var/lib/keys", false))).StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1");
        (plan.GetProperty("product").GetString(), plan.GetProperty("environment").GetString(), plan.GetProperty("release").GetString(),
                plan.GetProperty("firstDeploy").GetBoolean())
            .Should().Be(("foreverpin", "dev", "v1.0.0", true));
        var vault = plan.GetProperty("vault");
        (vault.GetProperty("id").GetString(), vault.GetProperty("namespace").GetString(), vault.GetProperty("reachable").GetBoolean(),
                vault.GetProperty("mode").GetString())
            .Should().Be(("built-in", "foreverpin-dev", true, "builtin"));
        var keys = Keys(plan, "desk");
        keys["AllowedHosts"].Should().Be(("derived", "desk.example;ocharo-platform-dev-desk;localhost"));
        keys["ConnectionStrings:Database"].Should().Be(("derived", null), "the target's own database fills it");
        keys["DataProtection:KeysPath"].Should().Be(("stored", "/var/lib/keys"));
        keys["ImageWorker:Key"].Should().Be(("vault", null), "a secret's value is never shown");
        keys["DataProtection:ApplicationName"].Should().Be(("missing", null));
        keys["Feature:Flag"].Should().Be(("default", "false"));
        plan.GetProperty("missing").GetInt32().Should().Be(1);
        var secret = Key(plan, "desk", "ImageWorker:Key").GetProperty("vault");
        (secret.GetProperty("version").GetInt32(), secret.GetProperty("updatedBy").GetString()).Should().Be((1, "test-admin"));
        secret.GetProperty("fingerprint").GetString().Should().MatchRegex("^[0-9a-f]{16}$");
        (await AdminClient.GetStringAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"))
            .Should().NotContain(Secret);

        // Once a deploy succeeded there, it is no longer the first.
        Fixture.Deployments.History.Add(Succeeded(Guid.NewGuid()));
        (await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"))
            .GetProperty("firstDeploy").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Plan_WithoutARelease_ShouldReadTheOneTheTargetRuns_ElseListOnlyStoredKeys()
    {
        await Save(("Feature:Banner", "hello", false));
        var bare = await DataAsync("/api/deployments/targets/foreverpin-dev/settings");
        bare.GetProperty("release").ValueKind.Should().Be(JsonValueKind.Null, "nothing ran there yet");
        bare.GetProperty("services").GetArrayLength().Should().Be(0);
        bare.GetProperty("extra").EnumerateArray().Select(key => key.GetProperty("key").GetString()).Should().Equal("Feature:Banner");

        Declare(hostFile: false);
        Fixture.Deployments.History.Add(Succeeded(Guid.NewGuid()));
        var running = await DataAsync("/api/deployments/targets/foreverpin-dev/settings");
        (running.GetProperty("release").GetString(), running.GetProperty("bundle").GetString()).Should().Be(("v1.0.0", "foreverpin-gh-1"));
        Keys(running, "desk").Should().ContainKey("ImageWorker:Key");
    }

    [Fact]
    public async Task Save_ShouldKeepSecretsInTheVault_StoreSettings_AndAuditKeysAndVersionsOnly()
    {
        Declare(hostFile: false);
        var response = await Save(("DataProtection:ApplicationName", "ocharo-desk", false), ("ImageWorker:Key", Secret, true));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (saved.GetProperty("settings").GetInt32(), saved.GetProperty("secrets").GetInt32()).Should().Be((1, 1));
        var written = saved.GetProperty("keys").EnumerateArray().Single(key => key.GetProperty("secret").GetBoolean());
        (written.GetProperty("key").GetString(), written.GetProperty("version").GetInt32()).Should().Be(("ImageWorker:Key", 1));
        Fixture.Vaults.Changes.Should().BeEmpty("the standalone vault no longer takes the environment's secrets");

        var keys = Keys(await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"), "desk");
        keys["DataProtection:ApplicationName"].Should().Be(("stored", "ocharo-desk"));
        keys["ImageWorker:Key"].Should().Be(("vault", null));

        var audit = (await DataAsync("/api/audit")).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "settings.save");
        (audit.GetProperty("subject").GetString(), audit.GetProperty("detail").GetString())
            .Should().Be(("foreverpin-dev", "DataProtection:ApplicationName setting, ImageWorker:Key secret v1"));
        (await AdminClient.GetStringAsync("/api/audit")).Should().NotContain(Secret);
    }

    [Fact]
    public async Task Save_OneFieldAtATime_ShouldKeepEachSave_AndAuditEach()
    {
        Declare(hostFile: false);
        (await Save(("DataProtection:ApplicationName", "ocharo-desk", false))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save(("ImageWorker:Key", Secret, true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save(("ImageWorker:Key", Secret + "-2", true))).StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1");
        plan.GetProperty("missing").GetInt32().Should().Be(1, "DataProtection:KeysPath alone is still missing");
        Key(plan, "desk", "ImageWorker:Key").GetProperty("vault").GetProperty("version").GetInt32().Should().Be(2);
        (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "settings.save")
            .Select(entry => entry.GetProperty("detail").GetString())
            .Should().BeEquivalentTo("DataProtection:ApplicationName setting", "ImageWorker:Key secret v1", "ImageWorker:Key secret v2");
    }

    [Fact]
    public async Task Generate_ShouldWriteARandomSecretVersion_ThatOnlyTheRunnerSees_AuditedWithoutItsValue()
    {
        DeclareTyped();
        var first = await Generate("Billing:SecretKey", "hex", note: "generated by Wheelhouse");
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var reply = await first.Content.ReadAsStringAsync();
        var written = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("keys").EnumerateArray().Single();
        (written.GetProperty("key").GetString(), written.GetProperty("version").GetInt32(), written.GetProperty("generated").GetBoolean())
            .Should().Be(("Billing:SecretKey", 1, true));

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var hex = Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("Billing:SecretKey").GetString()!;
        hex.Should().MatchRegex("^[0-9a-f]{64}$", "32 random bytes as lowercase hex");

        (await Generate("Billing:SecretKey", "base64url")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var base64 = Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("Billing:SecretKey").GetString()!;
        base64.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 random bytes as unpadded base64url");

        var history = (await DataAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions"))
            .GetProperty("versions").EnumerateArray().ToList();
        history.Select(version => version.GetProperty("version").GetInt32()).Should().Equal(2, 1);
        history[1].GetProperty("note").GetString().Should().Be("generated by Wheelhouse");
        history[0].GetProperty("fingerprint").GetString().Should().NotBe(history[1].GetProperty("fingerprint").GetString());
        (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "settings.save")
            .Select(entry => entry.GetProperty("detail").GetString())
            .Should().BeEquivalentTo("Billing:SecretKey secret v1 generated", "Billing:SecretKey secret v2 generated");
        foreach (var path in new[] { "/api/audit", "/api/deployments/targets/foreverpin-dev/settings",
                     "/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions" })
        {
            var body = await AdminClient.GetStringAsync(path);
            body.Should().NotContain(hex, path).And.NotContain(base64, path);
        }
        reply.Should().NotContain(hex);
    }

    [Theory]
    [InlineData("Billing:SecretKey", "hex", false, null, "only a secret's value is generated")]
    [InlineData("Billing:SecretKey", "hex", true, "given", "is generated; give no value")]
    [InlineData("Billing:SecretKey", "words", true, null, "a generated value is hex or base64url")]
    [InlineData("Limits:MaxUsers", "hex", true, null, "takes a number; only a text secret's value is generated")]
    public async Task Generate_ShouldRefuseAPlainSettingAGivenValueAnUnknownFormatOrANonTextKey(
        string key, string format, bool secret, string? value, string reason)
    {
        DeclareTyped();
        var response = await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings",
            new { values = new[] { new { key, value, secret, generate = format } } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(reason);
        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/secrets/" + key + "/versions"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing was written");
    }

    [Fact]
    public async Task Save_ShouldBeAllOrNothing_WhenOneValueIsRefused()
    {
        DeclareTyped();
        var response = await Save(("Billing:SecretKey", Secret, true), ("Limits:MaxUsers", "many", false));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Limits:MaxUsers must be a number").And.NotContain("many");

        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the secret of the refused save was never written");
        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1");
        Keys(plan, "api")["Billing:SecretKey"].Should().Be(("missing", null));
    }

    [Theory]
    [InlineData("Limits:MaxUsers", "50", true)]
    [InlineData("Limits:MaxUsers", "-0.5", true)]
    [InlineData("Limits:MaxUsers", "1e3", false)]
    [InlineData("Features:Beta", "true", true)]
    [InlineData("Features:Beta", "yes", false)]
    [InlineData("Billing:Mode", "live", true)]
    [InlineData("Billing:Mode", "sandbox", false)]
    [InlineData("Support:Url", "https://help.example/a?b=c", true)]
    [InlineData("Support:Url", "help.example", false)]
    [InlineData("Jobs:Cleanup", "0 3 * * *", true)]
    [InlineData("Jobs:Cleanup", "*/15 9-17 * * MON-FRI", true)]
    [InlineData("Jobs:Cleanup", "0 3 * *", false)]
    [InlineData("Promo:EndsOn", "31.01.2027", true)]
    [InlineData("Promo:EndsOn", "2027-01-31", false)]
    [InlineData("Support:Email", "owner@example.com", true)]
    [InlineData("Support:Email", "owner", false)]
    public async Task Save_ShouldCheckEachValueAgainstItsDeclaredType(string key, string value, bool fits)
    {
        DeclareTyped();
        var response = await Save((key, value, false));
        response.StatusCode.Should().Be(fits ? HttpStatusCode.OK : HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Save_ShouldStoreABooleanInLowercase()
    {
        DeclareTyped();
        (await Save(("Features:Beta", "True", false))).StatusCode.Should().Be(HttpStatusCode.OK);
        Keys(await DataAsync("/api/deployments/targets/foreverpin-dev/settings?release=foreverpin-gh-1"), "api")["Features:Beta"]
            .Should().Be(("stored", "true"));
    }

    [Fact]
    public async Task Save_ShouldRefuseASecretLookingKeyAsAPlainSetting_UnlessConfirmed()
    {
        var refused = await Save(("Smtp:Password", "hunter2", false));
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Smtp:Password reads as a secret");
        (await Save(("ConnectionStrings:Reports", "Host=x", false))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var confirmed = await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings",
            new { values = new[] { new { key = "Smtp:Password", value = "not-really-secret", secret = false, confirmPlain = true } } });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        // A key the release declares a secret needs the confirmation too, whatever its name.
        DeclareTyped();
        (await Save(("Stripe:Account", "acct_1", false))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // A typed key the release calls plain is plain, whatever its name says.
        (await Save(("Support:TokenUrl", "https://help.example/token", false))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Save_ShouldMoveAKeyBetweenLayers_AndRemoveAValue()
    {
        (await Save(("Feature:Key", "plain", false))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "it reads as a secret");
        (await Save(("Feature:Secret", Secret, true))).StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings");
        plan.GetProperty("extra").EnumerateArray().Single().GetProperty("source").GetString().Should().Be("vault");

        var removed = await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings",
            new { values = new[] { new { key = "Feature:Secret", value = "", secret = true, remove = true } } });
        removed.StatusCode.Should().Be(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        (await DataAsync("/api/deployments/targets/foreverpin-dev/settings")).GetProperty("extra").GetArrayLength()
            .Should().Be(0, "a removed secret renders nowhere");
        var history = await DataAsync("/api/deployments/targets/foreverpin-dev/secrets/Feature:Secret/versions");
        (history.GetProperty("removed").GetBoolean(), history.GetProperty("versions").GetArrayLength()).Should().Be((true, 1),
            "its versions stay for the audit");
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
    public async Task Deploy_ShouldWaitForARequiredKeyTheOwnerAdded()
    {
        (await Owner("settings").PutAsJsonAsync("/api/products/foreverpin/setting-keys/Support:Email",
            new { type = "email", required = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("every service: Support:Email");
        (await Save(("Support:Email", "owner", false))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an added key is typed too");
        (await Save(("Support:Email", "owner@example.com", false))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
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
        await Save(("DataProtection:ApplicationName", "ocharo-desk", false));
        await Save(("ImageWorker:Key", Secret, true));

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var values = Fixture.Deployments.LastSettings!.Value.GetProperty("values");
        values.GetProperty("DataProtection:ApplicationName").GetString().Should().Be("ocharo-desk");
        values.GetProperty("ImageWorker:Key").GetString().Should().Be(Secret);
        Fixture.Vaults.Reads.Should().BeEmpty("the render reads the built-in vault in process");
        (await AdminClient.GetStringAsync("/api/deployments/history?notApplied=true")).Should().NotContain(Secret);
        (await AdminClient.GetStringAsync("/api/audit")).Should().NotContain(Secret);
    }

    [Fact]
    public async Task Check_ShouldWeighTheKeysWheelhouseWouldSend()
    {
        await Save(("ImageWorker:Key", Secret, true));
        await Save(("DataProtection:ApplicationName", "ocharo-desk", false));
        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/check?release=foreverpin-gh-1"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastCheckKeys.Should().Equal("DataProtection:ApplicationName", "ImageWorker:Key");
    }

    /// <summary>A succeeded deploy of release v1.0.0 to foreverpin-dev, as the runner records it.</summary>
    private static object Succeeded(Guid id) => new
    {
        id = id.ToString(), targetId = "foreverpin-dev", environment = "dev", bundleId = "foreverpin-gh-1",
        release = "v1.0.0", actor = "test-admin", status = "succeeded", submittedAt = "2026-10-19T10:00:00+00:00",
    };

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

    /// <summary>Makes the runner declare one service, <c>api</c>, with typed keys as a v0.10 bundle carries them, in the
    /// release the target runs, so a save types its values against it.</summary>
    private void DeclareTyped()
    {
        Fixture.Deployments.History.Add(Succeeded(Guid.NewGuid()));
        Fixture.Deployments.Declarations = new
        {
            targetId = "foreverpin-dev", release = "v1.0.0", product = "foreverpin", environment = "dev",
            services = new[]
            {
                new
                {
                    name = "api", hostFile = false,
                    keys = new object[]
                    {
                        new { key = "Billing:SecretKey", required = true, secret = true },
                        new { key = "Stripe:Account", required = false, secret = true, type = "text" },
                        new { key = "Limits:MaxUsers", required = false, secret = false, type = "number", @default = "50" },
                        new { key = "Features:Beta", required = false, secret = false, type = "boolean" },
                        new { key = "Billing:Mode", required = false, secret = false, type = "choice", allowed = new[] { "test", "live" } },
                        new { key = "Support:Url", required = false, secret = false, type = "url" },
                        new { key = "Support:TokenUrl", required = false, secret = false, type = "url" },
                        new { key = "Jobs:Cleanup", required = false, secret = false, type = "cron" },
                        new { key = "Promo:EndsOn", required = false, secret = false, type = "date", format = "dd.MM.yyyy" },
                        new { key = "Support:Email", required = false, secret = false, type = "email" },
                    },
                },
            },
        };
    }

    private static JsonElement Key(JsonElement plan, string service, string key) =>
        plan.GetProperty("services").EnumerateArray().Single(item => item.GetProperty("name").GetString() == service)
            .GetProperty("keys").EnumerateArray().Single(item => item.GetProperty("key").GetString() == key);

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

    private Task<HttpResponseMessage> Generate(string key, string format, string? note = null) =>
        Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings",
            new { values = new[] { new { key, secret = true, generate = format, note } } });

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
