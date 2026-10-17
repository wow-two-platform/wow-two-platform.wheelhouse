using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Audit.Enums;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies that every operator action lands in the hash-chained audit trail, reads stay out of it, and no
/// secret value ever reaches it.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class AuditE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Theory]
    [InlineData("/api/audit")]
    [InlineData("/api/audit/verification")]
    public async Task Reads_RequireAdminAndAreNeverCached(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnonymousClient.GetAsync(path)).StatusCode);
        var response = await AdminClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "Audit responses must never be cached.");
    }

    [Fact]
    public async Task Deploy_RecordsWhoStartedWhichReleaseOnWhichTarget()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Deploy("pilot", "v1")).StatusCode);

        var entry = Assert.Single(await Entries());
        Assert.Equal((1L, "test-admin", "deployment.start", "pilot", "succeeded", "v1"),
            (entry.GetProperty("sequence").GetInt64(), entry.GetProperty("actor").GetString(),
                entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
                entry.GetProperty("outcome").GetString(), entry.GetProperty("detail").GetString()));
        Assert.False(entry.TryGetProperty("hash", out _), "Chain hashes stay server-side.");
    }

    [Fact]
    public async Task RefusedDeploy_IsRecordedWithTheRefusalReason()
    {
        Fixture.Deployments.StartRefusal = "Deploy this release to dev first, or type the target ID to skip the dev pass";

        Assert.Equal(HttpStatusCode.Conflict, (await Deploy("pilot-prod", "v2")).StatusCode);

        var entry = Assert.Single(await Entries());
        Assert.Equal(("failed", Fixture.Deployments.StartRefusal),
            (entry.GetProperty("outcome").GetString(), entry.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task InvalidRequest_IsRecordedAsFailedBeforeItReachesTheHandler()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var response = await client.PostAsJsonAsync("/api/integration-keys",
            new { name = "Claude", scopes = new[] { "deployments:write" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var entry = Assert.Single(await Entries());
        Assert.Equal(("integration-key.create", "Claude", "failed"),
            (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
                entry.GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task KeyCreation_IsRecordedWithItsScopesButNeverItsSecret()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var response = await client.PostAsJsonAsync("/api/integration-keys", new { name = "Codex", scopes = new[] { "catalog:read" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var secret = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString()!;

        Assert.DoesNotContain(secret, await AdminClient.GetStringAsync("/api/audit"));
        var entry = Assert.Single(await Entries());
        Assert.Equal(("test-admin", "integration-key.create", "Codex", "succeeded", "catalog:read"),
            (entry.GetProperty("actor").GetString(), entry.GetProperty("action").GetString(),
                entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString(),
                entry.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task StatusChange_IsRecordedAgainstTheProduct()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "status");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/products/foreverpin/status", new { status = "inactive" })).StatusCode);

        var entry = Assert.Single(await Entries());
        Assert.Equal(("product.status", "foreverpin", "succeeded", "Inactive"),
            (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
                entry.GetProperty("outcome").GetString(), entry.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task SecretChange_IsRecordedWithoutItsValue()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "vault");
        var response = await client.PutAsJsonAsync("/api/vaults/pilot-vault/secrets/billing/Billing%3ASecretKey",
            new { value = "sk_live_DO_NOT_AUDIT", description = "Stripe" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await AdminClient.GetStringAsync("/api/audit");
        Assert.DoesNotContain("DO_NOT_AUDIT", list);
        var entry = Assert.Single(await Entries());
        Assert.Equal(("vault.secret.set", "pilot-vault/billing", "set secret billing/Billing:SecretKey"),
            (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
                entry.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Reads_LeaveNoEntries()
    {
        await AdminClient.GetAsync("/api/deployments/targets");
        await AdminClient.GetAsync("/api/products");
        Assert.Empty(await Entries());
    }

    [Fact]
    public async Task List_PagesBackNewestFirst()
    {
        foreach (var release in new[] { "v1", "v2", "v3" })
            await Deploy("pilot", release);

        Assert.Equal([3L, 2L], (await Entries("?limit=2")).Select(e => e.GetProperty("sequence").GetInt64()));
        Assert.Equal([1L], (await Entries("?limit=2&before=2")).Select(e => e.GetProperty("sequence").GetInt64()));
        Assert.Equal(HttpStatusCode.BadRequest, (await AdminClient.GetAsync("/api/audit?limit=201")).StatusCode);
    }

    [Fact]
    public async Task List_KeepsOnlyTheEntriesAboutOneProduct()
    {
        await Deploy("foreverpin-dev", "v1");
        await Deploy("pilot", "v2");
        await Act("status", client => client.PutAsJsonAsync("/api/products/foreverpin/services/management/status", new { status = "inactive" }));
        await Act("vault", client => client.PutAsJsonAsync("/api/vaults/pilot-vault/secrets/foreverpin-dev/Billing%3ASecretKey",
            new { value = "sk_test_value", description = "Stripe" }));
        await Act("vault", client => client.PutAsJsonAsync("/api/vaults/pilot-vault/secrets/billing/Billing%3ASecretKey",
            new { value = "sk_test_value", description = "Stripe" }));
        await Act("status", client => client.PutAsJsonAsync("/api/products/wheelhouse/status", new { status = "inactive" }));
        await Act("status", client => client.PutAsJsonAsync("/api/products/foreverpin/status", new { status = "inactive" }));

        var subjects = (await Entries("?product=foreverpin")).Select(e => (e.GetProperty("action").GetString(), e.GetProperty("subject").GetString()));
        Assert.Equal(
            [("product.status", "foreverpin"), ("vault.secret.set", "pilot-vault/foreverpin-dev"),
                ("service.status", "foreverpin/management"), ("deployment.start", "foreverpin-dev")],
            subjects);
        Assert.Equal([4L], (await Entries("?product=foreverpin&limit=1&before=7")).Select(e => e.GetProperty("sequence").GetInt64()));
        Assert.Equal(["wheelhouse"], (await Entries("?product=wheelhouse")).Select(e => e.GetProperty("subject").GetString()));
        Assert.Empty(await Entries("?product=no-such-product"));
        Assert.Equal(7, (await Entries()).Length);
    }

    [Fact]
    public async Task Verification_PassesAnIntactChainAndNamesTheFirstEditedEntry()
    {
        await Deploy("pilot", "v1");
        await Deploy("pilot", "v2");
        var intact = await Verification();
        Assert.Equal((true, 2), (intact.GetProperty("intact").GetBoolean(), intact.GetProperty("entries").GetInt32()));

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        // The table refuses edits outright; only someone who can disable its trigger can rewrite a row.
        await Assert.ThrowsAsync<PostgresException>(() =>
            new NpgsqlCommand("UPDATE audit_entries SET subject = 'other' WHERE sequence = 1", connection).ExecuteNonQueryAsync());
        await new NpgsqlCommand(
            "ALTER TABLE audit_entries DISABLE TRIGGER audit_entries_append_only; " +
            "UPDATE audit_entries SET subject = 'other' WHERE sequence = 1; " +
            "ALTER TABLE audit_entries ENABLE TRIGGER audit_entries_append_only;", connection).ExecuteNonQueryAsync();

        var broken = await Verification();
        Assert.Equal((false, 1L, "HashMismatch"),
            (broken.GetProperty("intact").GetBoolean(), broken.GetProperty("brokenSequence").GetInt64(),
                broken.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Verification_PassesAnEarlierRowAndASubMicrosecondEntry_AfterTheDatabaseRoundTrip()
    {
        // Row 1 as the trail sealed it before SDK 10.0.65: a microsecond instant, hashed at its exact ticks.
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "INSERT INTO audit_entries " +
                "(id, sequence, previous_hash, hash, occurred_at_utc, actor, action, subject, outcome, detail, reason) " +
                "VALUES ('6f1d6c1e-0a4b-4e57-9a51-2d1f3b5c7a90', 1, ''::bytea, @hash, '2026-10-01T09:15:30.123456Z', " +
                "'test-admin', 'deployment.start', 'pilot', 'succeeded', 'v1', NULL)", connection);
            insert.Parameters.AddWithValue("hash",
                Convert.FromHexString("de5efce963e11e501d5753ed1452ed76c18296076ac97f5e3ac6f09ae4c112c1"));
            await insert.ExecuteNonQueryAsync();
        }

        // Row 2 sealed now at a sub-microsecond instant; the database keeps it to the microsecond.
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
        {
            var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero).AddTicks(1_234_567));
            var ticking = (IAuditTrail)ActivatorUtilities.CreateInstance(scope.ServiceProvider, trail.GetType(), clock);
            await ticking.AppendAsync(new AuditRecord("test-admin", "deployment.start", "pilot", AuditOutcome.Succeeded, "v2", null));
        }

        var verification = await Verification();
        Assert.Equal((true, 2), (verification.GetProperty("intact").GetBoolean(), verification.GetProperty("entries").GetInt32()));
    }

    [Fact]
    public async Task ConcurrentActions_ChainEveryEntryToTheOneCommittedBefore()
    {
        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(n => Deploy("pilot", $"v{n}")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));

        var verification = await Verification();
        Assert.Equal((true, 8), (verification.GetProperty("intact").GetBoolean(), verification.GetProperty("entries").GetInt32()));
    }

    private Task<HttpResponseMessage> Deploy(string target, string release)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        return client.PostAsJsonAsync("/api/deployments", new { target, release });
    }

    private async Task Act(string action, Func<HttpClient, Task<HttpResponseMessage>> send)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        Assert.Equal(HttpStatusCode.OK, (await send(client)).StatusCode);
    }

    private async Task<JsonElement[]> Entries(string query = "")
    {
        var document = await AdminClient.GetFromJsonAsync<JsonElement>("/api/audit" + query);
        return [.. document.GetProperty("data").EnumerateArray()];
    }

    private async Task<JsonElement> Verification() =>
        (await AdminClient.GetFromJsonAsync<JsonElement>("/api/audit/verification")).GetProperty("data");
}
