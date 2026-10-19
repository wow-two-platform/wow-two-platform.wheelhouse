using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the built-in Secrets Vault: the root key file unseals it (missing or foreign, it seals and a deploy that needs
/// secrets waits), values are encrypted at rest and never leave except to the runner, every change is a version with
/// who, when, fingerprint and note, a version restores as a new one, expiries warn in Issues, edits apply now or at a
/// set time as a deploy of the running release, a failed deploy names the settings it changed, the full check decrypts
/// everything, and imports from a standalone vault — over its API or from its database — re-encrypt and verify.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class SecretsVaultE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Secret = "sk_live_VAULT_7f3a";

    [Fact]
    public async Task Status_ShouldBeUnsealed_NameTheKeyFile_AndNeverShowTheKey()
    {
        var status = await DataAsync("/api/vault");
        (status.GetProperty("mode").GetString(), status.GetProperty("state").GetString(), status.GetProperty("keyFile").GetString())
            .Should().Be(("builtin", "unsealed", Fixture.VaultKeyFile));
        (await AdminClient.GetStringAsync("/api/vault")).Should().NotContain(Fixture.VaultKey);
        (await AnonymousClient.GetAsync("/api/vault")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Secrets_ShouldBeEncryptedAtRest_AndReachOnlyTheRunner()
    {
        (await Save("Billing:SecretKey", Secret)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var read = new NpgsqlCommand("SELECT cipher_text FROM vault.vault_secret_versions", connection);
            var stored = (byte[])(await read.ExecuteScalarAsync())!;
            Encoding.UTF8.GetString(stored).Should().NotContain(Secret);
            stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Secret)).Should().Be(-1);
        }
        foreach (var path in new[] { "/api/deployments/targets/foreverpin-dev/settings", "/api/vault", "/api/audit",
                     "/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions" })
            (await AdminClient.GetStringAsync(path)).Should().NotContain(Secret, path);

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("Billing:SecretKey").GetString().Should().Be(Secret);
    }

    [Fact]
    public async Task AMissingKeyFile_ShouldSealTheVault_AndHoldADeployThatNeedsSecrets()
    {
        (await Save("Billing:SecretKey", Secret)).StatusCode.Should().Be(HttpStatusCode.OK);
        File.Delete(Fixture.VaultKeyFile);

        var status = await DataAsync("/api/vault");
        (status.GetProperty("state").GetString(), status.GetProperty("reason").GetString()).Should().Be(("sealed", "missing"));
        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings");
        (plan.GetProperty("vault").GetProperty("reachable").GetBoolean(), plan.GetProperty("vault").GetProperty("sealed").GetString())
            .Should().Be((false, "missing"));
        plan.GetProperty("extra").EnumerateArray().Single().GetProperty("vault").GetProperty("version").GetInt32()
            .Should().Be(1, "metadata still reads while sealed");

        var deploy = await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" });
        deploy.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await deploy.Content.ReadAsStringAsync()).Should().Contain("The vault is sealed");
        Fixture.Deployments.Starts.Should().BeEmpty();
        (await Save("Billing:SecretKey", "another")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "nothing writes while sealed");
        var issues = await DataAsync("/api/vault/issues");
        (issues.GetProperty("state").GetString(), issues.GetProperty("reason").GetString()).Should().Be(("sealed", "missing"));

        // Placing the key file unseals it without a restart.
        await File.WriteAllTextAsync(Fixture.VaultKeyFile, Fixture.VaultKey + "\n");
        (await DataAsync("/api/vault")).GetProperty("state").GetString().Should().Be("unsealed");
        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AForeignOrMalformedKey_ShouldSealTheVault_UntilItsOwnKeyReturns()
    {
        (await Save("Billing:SecretKey", Secret)).StatusCode.Should().Be(HttpStatusCode.OK);
        await File.WriteAllTextAsync(Fixture.VaultKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var foreign = await DataAsync("/api/vault");
        (foreign.GetProperty("state").GetString(), foreign.GetProperty("reason").GetString()).Should().Be(("sealed", "mismatch"));
        (await Save("Billing:SecretKey", "under-the-wrong-key")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await File.WriteAllTextAsync(Fixture.VaultKeyFile, "not a key");
        (await DataAsync("/api/vault")).GetProperty("reason").GetString().Should().Be("invalid");

        await File.WriteAllTextAsync(Fixture.VaultKeyFile, Fixture.VaultKey);
        (await DataAsync("/api/vault")).GetProperty("state").GetString().Should().Be("unsealed");
        (await Save("Billing:SecretKey", "rotated")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Versions_ShouldRecordEachChange_AndRestoreAnEarlierOneAsNew()
    {
        (await Save("Billing:SecretKey", Secret, note: "first live key")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("Billing:SecretKey", Secret + "-rotated")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("Billing:SecretKey", Secret)).StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await DataAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions");
        var versions = history.GetProperty("versions").EnumerateArray().ToList();
        versions.Select(item => item.GetProperty("version").GetInt32()).Should().Equal(3, 2, 1);
        versions.Select(item => item.GetProperty("current").GetBoolean()).Should().Equal(true, false, false);
        versions.Should().OnlyContain(item => item.GetProperty("createdBy").GetString() == "test-admin");
        versions[2].GetProperty("note").GetString().Should().Be("first live key");
        var fingerprints = versions.Select(item => item.GetProperty("fingerprint").GetString()).ToList();
        fingerprints[0].Should().Be(fingerprints[2], "the same value under the same product key").And.NotBe(fingerprints[1]);

        var restored = await Owner("settings").PostAsJsonAsync(
            "/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/restore", new { version = 2, note = "roll back the key" });
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        var latest = (await DataAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions"))
            .GetProperty("versions")[0];
        (latest.GetProperty("version").GetInt32(), latest.GetProperty("origin").GetString(), latest.GetProperty("note").GetString(),
                latest.GetProperty("fingerprint").GetString())
            .Should().Be((4, "restored from v2", "roll back the key", fingerprints[1]));
        (await Owner("settings").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/restore", new { version = 4 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "it is already on that version");

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("Billing:SecretKey").GetString()
            .Should().Be(Secret + "-rotated");
        (await DataAsync("/api/audit")).EnumerateArray().Select(entry => entry.GetProperty("action").GetString())
            .Should().Contain("settings.secret.restore");
    }

    [Fact]
    public async Task Expiry_ShouldWarnAt30And7Days_AndFlagExpiredSecrets()
    {
        var now = Fixture.Host.Clock.GetUtcNow();
        (await Save("Billing:SecretKey", Secret, expiresAt: now.AddDays(20))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("Billing:WebhookSecret", Secret, expiresAt: now.AddDays(5))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("Mail:ApiKey", Secret, expiresAt: now.AddDays(90))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("Old:Token", Secret)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/secrets/Old:Token/expiry",
            new { expiresAt = now.AddDays(-1) })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var issues = (await DataAsync("/api/vault/issues")).GetProperty("expiries").EnumerateArray()
            .ToDictionary(item => item.GetProperty("key").GetString()!, item => item.GetProperty("level").GetString());
        issues.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["Old:Token"] = "expired", ["Billing:WebhookSecret"] = "7d", ["Billing:SecretKey"] = "30d",
        });
        var status = await DataAsync("/api/vault");
        (status.GetProperty("expiring").GetInt32(), status.GetProperty("expired").GetInt32()).Should().Be((2, 1));
        var plan = await DataAsync("/api/deployments/targets/foreverpin-dev/settings");
        plan.GetProperty("extra").EnumerateArray().Single(key => key.GetProperty("key").GetString() == "Old:Token")
            .GetProperty("vault").GetProperty("expiry").GetString().Should().Be("expired");

        // A new value keeps the expiry unless the save names one; clearing it ends the warning.
        (await Save("Billing:SecretKey", Secret + "-2")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/secrets/Old:Token/expiry", new { expiresAt = (DateTimeOffset?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DataAsync("/api/vault/issues")).GetProperty("expiries").EnumerateArray()
            .Select(item => item.GetProperty("key").GetString()).Should().BeEquivalentTo("Billing:SecretKey", "Billing:WebhookSecret");
    }

    [Fact]
    public async Task ApplyNow_ShouldDeployTheRunningReleaseAgain_AsASettingsDeployment()
    {
        (await Owner("settings").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings/apply", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "nothing runs there yet");

        await Save("Billing:SecretKey", Secret);
        var first = await DeployAsync();
        Fixture.Deployments.History.Add(Record(first, "succeeded"));
        (await DataAsync("/api/deployments/targets/foreverpin-dev/settings")).GetProperty("unapplied").GetArrayLength().Should().Be(0);

        await Save("Billing:SecretKey", Secret + "-2");
        (await DataAsync("/api/deployments/targets/foreverpin-dev/settings")).GetProperty("unapplied").EnumerateArray()
            .Select(key => key.GetString()).Should().Equal("Billing:SecretKey");

        var applied = await Owner("settings").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings/apply",
            new { reason = "rotated the Stripe key" });
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var data = (await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (data.GetProperty("mode").GetString(), data.GetProperty("release").GetString()).Should().Be(("now", "v1.0.0"));
        Fixture.Deployments.Starts.Last().Should().Be(("foreverpin-dev", "foreverpin-gh-1", "test-admin"));
        Fixture.Deployments.LastReason.Should().Be("Settings apply: rotated the Stripe key");
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("Billing:SecretKey").GetString().Should().Be(Secret + "-2");
        (await DataAsync("/api/audit")).EnumerateArray().Select(entry => entry.GetProperty("action").GetString())
            .Should().Contain(["settings.apply", "deployment.start"]);
    }

    [Fact]
    public async Task ApplyAt_ShouldScheduleOnePerTarget_AndRunItWhenDue()
    {
        await Save("Billing:SecretKey", Secret);
        Fixture.Deployments.History.Add(Record(await DeployAsync(), "succeeded"));
        var at = Fixture.Host.Clock.GetUtcNow().AddHours(2);
        var scheduled = await Owner("settings").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings/apply", new { at });
        scheduled.StatusCode.Should().Be(HttpStatusCode.OK, await scheduled.Content.ReadAsStringAsync());
        (await scheduled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("mode").GetString().Should().Be("scheduled");
        var pending = (await DataAsync("/api/deployments/targets/foreverpin-dev/settings")).GetProperty("pending");
        (pending.GetProperty("by").GetString(), pending.GetProperty("at").GetDateTimeOffset()).Should().Be(("test-admin", at.ToUniversalTime()));
        var starts = Fixture.Deployments.Starts.Count;

        // Nothing is due yet; once due, the pass deploys the running release for the owner who scheduled it.
        await RunDueAsync();
        Fixture.Deployments.Starts.Should().HaveCount(starts);
        Fixture.Host.Clock.Advance(TimeSpan.FromHours(3));
        await RunDueAsync();
        Fixture.Deployments.Starts.Should().HaveCount(starts + 1);
        Fixture.Deployments.LastReason.Should().Be("Settings apply");
        Fixture.Deployments.LastActor.Should().Be("test-admin", "the apply acts for the owner who scheduled it");
        (await DataAsync("/api/deployments/targets/foreverpin-dev/settings")).GetProperty("pending").ValueKind.Should().Be(JsonValueKind.Null);
        (await DataAsync("/api/audit")).EnumerateArray()
            .Any(entry => entry.GetProperty("action").GetString() == "deployment.start"
                          && entry.GetProperty("detail").GetString()!.Contains("scheduled: applies edited settings")).Should().BeTrue();

        (await Owner("settings").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings/apply",
            new { at = Fixture.Host.Clock.GetUtcNow().AddHours(2) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("settings").DeleteAsync("/api/deployments/targets/foreverpin-dev/settings/apply"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Owner("settings").DeleteAsync("/api/deployments/targets/foreverpin-dev/settings/apply"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AFailedDeployThatChangedSettings_ShouldNameThemAsTheLikelyCause()
    {
        await Save("Billing:SecretKey", Secret);
        await SavePlain("Billing:Mode", "test");
        var first = await DeployAsync();
        Fixture.Deployments.History.Add(Record(first, "succeeded", "2026-10-19T10:00:00+00:00"));

        await Save("Billing:SecretKey", Secret + "-2");
        await SavePlain("Billing:Mode", "live");
        await SavePlain("Billing:Region", "eu");
        var second = await DeployAsync();
        Fixture.Deployments.History.Add(Record(second, "rolled_back", "2026-10-19T11:00:00+00:00"));

        var item = (await DataAsync("/api/deployments/" + second + "/detail")).GetProperty("deployment");
        item.GetProperty("settingsSuspect").GetBoolean().Should().BeTrue();
        var changed = item.GetProperty("settingsChanged").EnumerateArray()
            .ToDictionary(change => change.GetProperty("key").GetString()!, change => change);
        changed.Keys.Should().BeEquivalentTo("Billing:Mode", "Billing:Region", "Billing:SecretKey");
        (changed["Billing:SecretKey"].GetProperty("kind").GetString(), changed["Billing:SecretKey"].GetProperty("from").GetInt32(),
                changed["Billing:SecretKey"].GetProperty("to").GetInt32())
            .Should().Be(("secret", 1, 2));
        changed["Billing:Region"].GetProperty("change").GetString().Should().Be("added");

        var issue = (await DataAsync("/api/deployments/issues")).EnumerateArray().Single();
        issue.GetProperty("deployment").GetProperty("settingsSuspect").GetBoolean().Should().BeTrue();
        (await DataAsync("/api/deployments/" + first + "/detail")).GetProperty("deployment").GetProperty("settingsChanged")
            .ValueKind.Should().Be(JsonValueKind.Null, "the first render had nothing to compare with");
        (await AdminClient.GetStringAsync("/api/deployments/" + second + "/detail")).Should().NotContain(Secret).And.NotContain("live");
    }

    [Fact]
    public async Task Verify_ShouldDecryptEverything_AndAuditTheCounts()
    {
        await Save("Billing:SecretKey", Secret);
        await Save("Billing:SecretKey", Secret + "-2");
        await Save("Billing:WebhookSecret", Secret, target: "foreverpin-prod");

        var result = await Owner("vault").PostAsync("/api/vault/verify", null);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (data.GetProperty("state").GetString(), data.GetProperty("canary").GetBoolean(), data.GetProperty("products").GetInt32(),
                data.GetProperty("secrets").GetInt32(), data.GetProperty("versions").GetInt32(), data.GetProperty("failed").GetInt32())
            .Should().Be(("unsealed", true, 1, 2, 3, 0));
        (await DataAsync("/api/audit")).EnumerateArray().Single(entry => entry.GetProperty("action").GetString() == "vault.verify")
            .GetProperty("detail").GetString().Should().Be("unsealed, canary verified, 1 product keys, 2 secrets, 3 versions decrypted, 0 failed");
    }

    [Fact]
    public async Task AnIntegrationKey_ShouldNeverReachTheVault()
    {
        var created = await Owner("key-create").PostAsJsonAsync("api/integration-keys", new { name = "Claude", scopes = new[] { "catalog:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        (await client.GetAsync("/api/vault")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ImportFromAStandaloneVaultsApi_ShouldCopyCurrentValues_VerifyThem_AndSkipThemNextTime()
    {
        Fixture.Vaults.Values["foreverpin-prod"] = new Dictionary<string, string> { ["Billing:SecretKey"] = Secret, ["Mail:ApiKey"] = "mail" };
        Fixture.Vaults.Values["ghost-dev"] = new Dictionary<string, string> { ["A:B"] = "x" };
        var response = await Owner("vault").PostAsJsonAsync("/api/vault/import", new { vault = "pilot-vault" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (report.GetProperty("namespaces").GetInt32(), report.GetProperty("secrets").GetInt32(), report.GetProperty("verified").GetInt32(),
                report.GetProperty("failed").GetInt32())
            .Should().Be((1, 2, 2, 0));
        report.GetProperty("unmatched").EnumerateArray().Select(item => item.GetString()).Should().Equal("ghost-dev");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Secret);

        var history = await DataAsync("/api/deployments/targets/foreverpin-prod/secrets/Billing:SecretKey/versions");
        history.GetProperty("versions")[0].GetProperty("origin").GetString().Should().Be("imported from pilot-vault/foreverpin-prod v1");
        var again = (await (await Owner("vault").PostAsJsonAsync("/api/vault/import", new { vault = "pilot-vault" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (again.GetProperty("secrets").GetInt32(), again.GetProperty("skipped").GetInt32()).Should().Be((0, 2));
        (await DataAsync("/api/audit")).EnumerateArray().Count(entry => entry.GetProperty("action").GetString() == "vault.import")
            .Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportFromAStandaloneVaultsDatabase_ShouldKeepEveryVersion_UnderEachProductsKey(bool sameKey)
    {
        var master = sameKey ? Convert.FromBase64String(Fixture.VaultKey) : RandomNumberGenerator.GetBytes(32);
        var source = await StandaloneDatabaseAsync(master);

        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundActor>().Actor = "cli";
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // A wrong master key decrypts nothing and keeps nothing.
        var wrong = await sender.SendAsync(new VaultDatabaseImportCommand(source, RandomNumberGenerator.GetBytes(32)));
        var refused = wrong.Should().BeOfType<AppResult<VaultImportDto>.Success>().Subject.Data;
        (refused.Secrets, refused.Versions, refused.Failed).Should().Be((0, 0, 2));
        (await AdminClient.GetAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var result = await sender.SendAsync(new VaultDatabaseImportCommand(source, sameKey ? null : (byte[])master.Clone()));
        var report = result.Should().BeOfType<AppResult<VaultImportDto>.Success>().Subject.Data;
        (report.Namespaces, report.Secrets, report.Versions, report.Verified, report.Failed).Should().Be((2, 2, 3, 3, 0));
        report.Unmatched.Should().Equal("unknown-dev");

        var history = await DataAsync("/api/deployments/targets/foreverpin-dev/secrets/Billing:SecretKey/versions");
        var versions = history.GetProperty("versions").EnumerateArray().ToList();
        versions.Select(item => (item.GetProperty("version").GetInt32(), item.GetProperty("createdBy").GetString()))
            .Should().Equal((2, "vault-admin"), (1, "vault-admin"));
        versions[1].GetProperty("createdAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        (await DataAsync("/api/deployments/targets/foreverpin-prod/secrets/Billing:WebhookSecret/versions"))
            .GetProperty("removed").GetBoolean().Should().BeTrue("it was disabled there");

        (await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty("Billing:SecretKey").GetString().Should().Be("dev-v2");
        var verified = (await (await Owner("vault").PostAsync("/api/vault/verify", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (verified.GetProperty("versions").GetInt32(), verified.GetProperty("failed").GetInt32()).Should().Be((3, 0));
        (await DataAsync("/api/audit")).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "vault.import")
            .Should().OnlyContain(entry => entry.GetProperty("actor").GetString() == "cli").And.HaveCount(2);
    }

    /// <summary>A standalone vault's database beside the run's own: foreverpin-dev with two versions of one secret,
    /// foreverpin-prod with a disabled one, and a namespace no product owns; encrypted as the standalone vault does.</summary>
    private async Task<string> StandaloneDatabaseAsync(byte[] master)
    {
        var name = "standalone_vault_" + Guid.NewGuid().ToString("N")[..8];
        await using (var admin = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE " + name, admin);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = name }.ConnectionString;
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await Execute(db, """
            CREATE TABLE namespaces (id uuid PRIMARY KEY, slug text NOT NULL, name text NOT NULL, created_at_utc timestamptz NOT NULL);
            CREATE TABLE data_keys (id uuid PRIMARY KEY, namespace_id uuid NOT NULL, wrapped_key bytea NOT NULL, nonce bytea NOT NULL,
                tag bytea NOT NULL, is_active boolean NOT NULL, created_at_utc timestamptz NOT NULL);
            CREATE TABLE secrets (id uuid PRIMARY KEY, namespace_id uuid NOT NULL, key text NOT NULL, description text NULL,
                state text NOT NULL, current_version integer NOT NULL, created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL);
            CREATE TABLE secret_versions (id uuid PRIMARY KEY, secret_id uuid NOT NULL, version integer NOT NULL, data_key_id uuid NOT NULL,
                nonce bytea NOT NULL, tag bytea NOT NULL, cipher_text bytea NOT NULL, created_at_utc timestamptz NOT NULL, created_by text NOT NULL);
            """);
        var cipher = Fixture.Host.Services.GetRequiredService<IValueCipher>();
        async Task<(Guid Space, Guid Key, byte[] DataKey)> Namespace(string slug)
        {
            var space = Guid.NewGuid();
            var key = Guid.NewGuid();
            var dataKey = RandomNumberGenerator.GetBytes(32);
            var wrapped = cipher.Encrypt(dataKey, master, "dek:" + slug);
            await Execute(db, "INSERT INTO namespaces VALUES (@id, @slug, @slug, now())", ("id", space), ("slug", slug));
            await Execute(db, "INSERT INTO data_keys VALUES (@id, @ns, @w, @n, @t, true, now())",
                ("id", key), ("ns", space), ("w", wrapped.CipherText), ("n", wrapped.Nonce), ("t", wrapped.Tag));
            return (space, key, dataKey);
        }
        async Task Add((Guid Space, Guid Key, byte[] DataKey) space, string slug, string key, string state, params string[] values)
        {
            var secret = Guid.NewGuid();
            await Execute(db, "INSERT INTO secrets VALUES (@id, @ns, @key, null, @state, @current, now(), now())",
                ("id", secret), ("ns", space.Space), ("key", key), ("state", state), ("current", values.Length));
            for (var index = 0; index < values.Length; index++)
            {
                var sealedValue = cipher.Encrypt(Encoding.UTF8.GetBytes(values[index]), space.DataKey, slug + "/" + key);
                await Execute(db, "INSERT INTO secret_versions VALUES (@id, @secret, @version, @key, @n, @t, @c, @at, 'vault-admin')",
                    ("id", Guid.NewGuid()), ("secret", secret), ("version", index + 1), ("key", space.Key), ("n", sealedValue.Nonce),
                    ("t", sealedValue.Tag), ("c", sealedValue.CipherText), ("at", new DateTimeOffset(2026, 9, 1 + index, 8, 0, 0, TimeSpan.Zero)));
            }
        }
        var dev = await Namespace("foreverpin-dev");
        await Add(dev, "foreverpin-dev", "Billing:SecretKey", "active", "dev-v1", "dev-v2");
        var prod = await Namespace("foreverpin-prod");
        await Add(prod, "foreverpin-prod", "Billing:WebhookSecret", "disabled", "prod-v1");
        var unknown = await Namespace("unknown-dev");
        await Add(unknown, "unknown-dev", "A:B", "active", "x");
        return connection;
    }

    private static async Task Execute(NpgsqlConnection db, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RunDueAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SettingsApplyService>().RunDueAsync(CancellationToken.None);
    }

    private async Task<string> DeployAsync()
    {
        var response = await Owner("deploy").PostAsJsonAsync("/api/deployments", new { target = "foreverpin-dev", release = "foreverpin-gh-1" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static object Record(string id, string status, string at = "2026-10-19T10:00:00+00:00") => new
    {
        id, targetId = "foreverpin-dev", environment = "dev", bundleId = "foreverpin-gh-1", release = "v1.0.0",
        actor = "test-admin", status, submittedAt = at, completedAt = at,
        mutationStarted = status != "succeeded",
    };

    private Task<HttpResponseMessage> Save(string key, string value, string? note = null, DateTimeOffset? expiresAt = null,
        string target = "foreverpin-dev") =>
        Owner("settings").PutAsJsonAsync("/api/deployments/targets/" + target + "/settings", new
        {
            values = new[]
            {
                new { key, value, secret = true, note, expiry = expiresAt is { } at ? new { at = (DateTimeOffset?)at } : null },
            },
        });

    private async Task SavePlain(string key, string value) =>
        (await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings",
            new { values = new[] { new { key, value, secret = false } } })).StatusCode.Should().Be(HttpStatusCode.OK);

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
