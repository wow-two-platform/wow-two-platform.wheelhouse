using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.BackgroundServices;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the root key's scheduled checks (decision 67). The offline copy: the status shows the key in use's fingerprint
/// (a labelled hash, never the key), Issues remind the owner to compare their copy once something is encrypted under the
/// key and until they mark it verified, the mark is audited with its reason and counts for the interval only while the key
/// in use keeps that fingerprint. The key file: a scheduled pass checks it is present, readable by the console alone and
/// opens the canary; a failure is an issue until a check passes again.
/// </summary>
/// <remarks>The scheduled check stays off under test (<c>Vault__KeyCheckMinutes</c> in <c>tests.runsettings</c>); each test runs
/// it itself. The verifications live in the vault schema, which the shared reset leaves alone, so each test here empties
/// them and places the run's key the way the owner does, closed to all but its owner.</remarks>
[Collection(WheelhouseCollection.Name)]
public sealed class RootKeyChecksE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture), IAsyncLifetime
{
    private const string Reason = "compared the printed copy in the safe";

    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var truncate = new NpgsqlCommand("TRUNCATE vault.vault_root_key_verifications", connection);
            await truncate.ExecuteNonQueryAsync();
        }
        await WriteKeyAsync(Fixture.VaultKey);
    }

    [Fact]
    public async Task Status_ShouldShowAStableLabelledFingerprint_AndNeverTheKey()
    {
        var expected = Fingerprint(Fixture.VaultKey);
        var copy = (await DataAsync("/api/vault")).GetProperty("offlineCopy");
        copy.GetProperty("fingerprint").GetString().Should().Be(expected);
        expected.Should().MatchRegex(KeyFingerprintHasher.Pattern);
        expected.Replace(" ", "", StringComparison.Ordinal).Should()
            .NotBe(Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(Fixture.VaultKey)))[..20], "the label sets it apart");
        (await DataAsync("/api/vault")).GetProperty("offlineCopy").GetProperty("fingerprint").GetString().Should().Be(expected);
        copy.GetProperty("intervalDays").GetInt32().Should().Be(90);

        var body = await AdminClient.GetStringAsync("/api/vault") + await AdminClient.GetStringAsync("/api/vault/issues");
        body.Should().NotContain(Fixture.VaultKey).And.NotContain(Convert.ToHexString(Convert.FromBase64String(Fixture.VaultKey)));

        var other = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await WriteKeyAsync(other);
        (await DataAsync("/api/vault")).GetProperty("offlineCopy").GetProperty("fingerprint").GetString()
            .Should().Be(Fingerprint(other), "an empty vault takes any key, and the fingerprint follows it");
    }

    [Fact]
    public async Task OfflineCopy_ShouldBeDueOnceTheKeyIsBound_AndItsVerificationAuditedWithTheReason()
    {
        (await DataAsync("/api/vault")).GetProperty("offlineCopy").GetProperty("due").GetBoolean()
            .Should().BeFalse("nothing is encrypted under the key yet");
        (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy").ValueKind.Should().Be(JsonValueKind.Null);

        await SaveSecretAsync();
        var due = (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy");
        (due.GetProperty("due").GetBoolean(), due.GetProperty("verifiedAt").ValueKind, due.GetProperty("changed").GetBoolean())
            .Should().Be((true, JsonValueKind.Null, false));

        var fingerprint = Fingerprint(Fixture.VaultKey);
        (await AdminClient.PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint, reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "an owner write names its action");
        (await Owner().PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint, reason = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the owner says how they checked it");
        (await Owner().PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint = Fixture.VaultKey, reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "only a fingerprint is taken");
        (await Owner().PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint = "0000 0000 0000 0000 0000", reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "the copy is compared with the key in use");

        var verified = await Owner().PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint, reason = Reason });
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        var now = Fixture.Host.Clock.GetUtcNow();
        var copy = (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (copy.GetProperty("due").GetBoolean(), copy.GetProperty("verifiedBy").GetString(), copy.GetProperty("reason").GetString(),
                copy.GetProperty("verifiedAt").GetDateTimeOffset(), copy.GetProperty("dueAt").GetDateTimeOffset())
            .Should().Be((false, "test-admin", Reason, now, now.AddDays(90)));
        (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy").ValueKind.Should().Be(JsonValueKind.Null);

        var entries = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "vault.offline-copy.verify").ToList();
        entries.Select(entry => (entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString()))
            .Should().AllSatisfy(item => item.Item1.Should().Be("root-key"));
        entries.Single(entry => entry.GetProperty("outcome").GetString() == "succeeded").GetProperty("detail").GetString()
            .Should().Be(Reason);
        entries.Count(entry => entry.GetProperty("outcome").GetString() == "failed")
            .Should().Be(3, "refusals past the action header are audited too");
        (await AdminClient.GetStringAsync("/api/audit")).Should().NotContain(Fixture.VaultKey);
    }

    [Fact]
    public async Task TheReminder_ShouldReturnOnceTheIntervalPasses()
    {
        await SaveSecretAsync();
        await VerifyAsync();
        Fixture.Host.Clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromMinutes(1));
        (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy").ValueKind.Should().Be(JsonValueKind.Null);

        Fixture.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        var due = (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy");
        (due.GetProperty("due").GetBoolean(), due.GetProperty("verifiedBy").GetString()).Should().Be((true, "test-admin"));
        (await DataAsync("/api/vault")).GetProperty("offlineCopy").GetProperty("due").GetBoolean().Should().BeTrue();

        await VerifyAsync();
        (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ANewKeyInUse_ShouldNoLongerCountTheLastVerification()
    {
        await VerifyAsync();
        var rotated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await WriteKeyAsync(rotated);
        await SaveSecretAsync();

        var copy = (await DataAsync("/api/vault")).GetProperty("offlineCopy");
        (copy.GetProperty("fingerprint").GetString(), copy.GetProperty("changed").GetBoolean(), copy.GetProperty("due").GetBoolean(),
                copy.GetProperty("verifiedAt").ValueKind)
            .Should().Be((Fingerprint(rotated), true, true, JsonValueKind.Null));
        (await DataAsync("/api/vault/issues")).GetProperty("offlineCopy").GetProperty("changed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task OnlyTheOwner_ShouldMarkTheCopyVerified_AndNotWhileSealed()
    {
        var created = await Owner("key-create").PostAsJsonAsync("api/integration-keys", new { name = "Claude", scopes = new[] { "catalog:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var key = AnonymousClient;
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        key.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "vault");
        (await key.PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint = Fingerprint(Fixture.VaultKey), reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        File.Delete(Fixture.VaultKeyFile);
        (await Owner().PostAsJsonAsync("/api/vault/offline-copy", new { fingerprint = Fingerprint(Fixture.VaultKey), reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await DataAsync("/api/vault")).GetProperty("offlineCopy").GetProperty("fingerprint").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheKeyFileCheck_ShouldPass_RaiseAnIssueOnFailure_AndClearOnTheNextGoodCheck()
    {
        Fixture.Host.Services.GetServices<IHostedService>().OfType<PeriodicJobBackgroundService>().Select(job => job.Name)
            .Should().Contain("vault-key-check");
        await SaveSecretAsync();
        try
        {
            (await CheckAsync()).Should().BeNull();
            var status = (await DataAsync("/api/vault")).GetProperty("keyFileCheck");
            (status.GetProperty("problem").ValueKind, status.GetProperty("at").GetDateTimeOffset())
                .Should().Be((JsonValueKind.Null, Fixture.Host.Clock.GetUtcNow()));
            (await DataAsync("/api/vault/issues")).GetProperty("keyFile").ValueKind.Should().Be(JsonValueKind.Null);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Fixture.VaultKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
                (await CheckAsync()).Should().Be("exposed");
                var issue = (await DataAsync("/api/vault/issues")).GetProperty("keyFile");
                issue.GetProperty("problem").GetString().Should().Be("exposed");
                (await DataAsync("/api/vault")).GetProperty("state").GetString().Should().Be("unsealed", "an exposed key still unseals");

                File.SetUnixFileMode(Fixture.VaultKeyFile, UnixFileMode.UserRead);
                (await CheckAsync()).Should().BeNull();
                (await DataAsync("/api/vault/issues")).GetProperty("keyFile").ValueKind.Should().Be(JsonValueKind.Null, "a good check clears it");

                if (Environment.UserName != "root")
                {
                    File.SetUnixFileMode(Fixture.VaultKeyFile, UnixFileMode.None);
                    (await CheckAsync()).Should().Be("unreadable");
                    (await DataAsync("/api/vault/issues")).GetProperty("keyFile").GetProperty("problem").GetString().Should().Be("unreadable");
                    File.SetUnixFileMode(Fixture.VaultKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            await WriteKeyAsync(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            (await CheckAsync()).Should().Be("mismatch");
            var sealedIssues = await DataAsync("/api/vault/issues");
            (sealedIssues.GetProperty("reason").GetString(), sealedIssues.GetProperty("keyFile").ValueKind)
                .Should().Be(("mismatch", JsonValueKind.Null), "the sealed vault already names the cause");
            (await DataAsync("/api/vault")).GetProperty("keyFileCheck").GetProperty("problem").GetString().Should().Be("mismatch");

            File.Delete(Fixture.VaultKeyFile);
            (await CheckAsync()).Should().Be("missing");
        }
        finally
        {
            await WriteKeyAsync(Fixture.VaultKey);
        }
        (await CheckAsync()).Should().BeNull();
        var cleared = await DataAsync("/api/vault/issues");
        (cleared.GetProperty("reason").ValueKind, cleared.GetProperty("keyFile").ValueKind).Should().Be((JsonValueKind.Null, JsonValueKind.Null));
    }

    [Fact]
    public async Task AFreshVault_ShouldRaiseNoKeyIssue_WhateverTheCheckFinds()
    {
        File.Delete(Fixture.VaultKeyFile);
        try
        {
            (await CheckAsync()).Should().Be("missing");
            var issues = await DataAsync("/api/vault/issues");
            (issues.GetProperty("reason").ValueKind, issues.GetProperty("keyFile").ValueKind, issues.GetProperty("offlineCopy").ValueKind)
                .Should().Be((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), "a lost key costs nothing yet");
        }
        finally
        {
            await WriteKeyAsync(Fixture.VaultKey);
            await CheckAsync();
        }
    }

    /// <summary>Writes a root key to the run's key file as the owner places one: closed to everyone but its owner, whatever
    /// mode a test left it in.</summary>
    private async Task WriteKeyAsync(string key)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(Fixture.VaultKeyFile))
            File.SetUnixFileMode(Fixture.VaultKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await File.WriteAllTextAsync(Fixture.VaultKeyFile, key + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Fixture.VaultKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>The fingerprint as the owner's offline-copy command prints it, computed here apart from the console.</summary>
    private static string Fingerprint(string base64Key)
    {
        var hex = Convert.ToHexStringLower(SHA256.HashData(
            [.. Encoding.ASCII.GetBytes("wheelhouse.vault.root-key.v1"), 0, .. Convert.FromBase64String(base64Key)]))[..20];
        return string.Join(' ', Enumerable.Range(0, 5).Select(group => hex.Substring(group * 4, 4)));
    }

    private async Task<string?> CheckAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<RootKeyChecks>().CheckFileAsync(CancellationToken.None)).Problem;
    }

    private async Task VerifyAsync()
    {
        var response = await Owner().PostAsJsonAsync("/api/vault/offline-copy",
            new { fingerprint = (await DataAsync("/api/vault")).GetProperty("offlineCopy").GetProperty("fingerprint").GetString(), reason = Reason });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task SaveSecretAsync()
    {
        var response = await Owner("settings").PutAsJsonAsync("/api/deployments/targets/foreverpin-dev/settings",
            new { values = new[] { new { key = "Billing:SecretKey", value = "sk_test_ROOT_KEY_CHECK", secret = true } } });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private HttpClient Owner(string action = "vault")
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
