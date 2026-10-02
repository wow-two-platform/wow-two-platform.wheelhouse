using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Infrastructure.Vaults;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Vaults.Entities;
using Wheelhouse.Tests.Unit.Fakes;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Vaults;

/// <summary>
/// Tests for <see cref="VaultGateway"/> over a scripted vault: one administrator session is reused, a rejected
/// session gets exactly one fresh sign-in, and vault failures map to operator-safe errors.
/// </summary>
public sealed class VaultGatewayTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-vault-").FullName;
    private readonly ScriptedVault _vault = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-09-25T12:00:00Z"));

    public VaultGatewayTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vaults", "pilot-vault"));
        File.WriteAllText(Path.Combine(_root, "vaults", "pilot-vault", "password"), "correct-horse-battery\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private VaultGateway Gateway(VaultSessionCache? cache = null)
    {
        var servers = new InMemoryServersRepository();
        var server = new ServerEntity
        {
            Id = Guid.NewGuid(), Slug = "pilot", Name = "Pilot", Host = "vps.example.net", Region = "hel1", SshUser = "deploy",
            Ingress = new ServerIngressValueObject(),
        };
        servers.Rows.Add(server);
        var vaults = new InMemoryVaultsRepository();
        vaults.Rows.Add(new VaultEntity
        {
            Id = Guid.NewGuid(), Slug = "pilot-vault", Name = "Pilot vault", ServerId = server.Id, Url = "http://vault.internal:8080",
        });
        return new VaultGateway(vaults, servers, new ClientFactory(_vault), cache ?? new VaultSessionCache(_time),
            new DeploymentSettings { Root = _root });
    }

    [Fact]
    public async Task One_session_serves_consecutive_calls()
    {
        var gateway = Gateway();

        await gateway.ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default);
        await gateway.ReadAsync(VaultResource.Secrets, "pilot-vault", "billing", default);

        _vault.SignIns.Should().Be(1);
        _vault.Requests.Should().Contain("GET /api/admin/secrets?ns=billing");
    }

    [Fact]
    public async Task A_rejected_session_gets_one_fresh_sign_in()
    {
        var gateway = Gateway();
        await gateway.ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default);
        _vault.RejectNextAdminCall = true;

        var result = await gateway.ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default);

        result.IsSuccess.Should().BeTrue();
        _vault.SignIns.Should().Be(2);
    }

    [Fact]
    public async Task Secret_values_travel_only_in_the_request_body()
    {
        var result = await Gateway().ApplyAsync("pilot-vault",
            new SecretSetChange { Namespace = "billing", Key = "Billing:SecretKey", Value = "sk_live_DO_NOT_LOG" }, default);

        result.IsSuccess.Should().BeTrue();
        _vault.Requests.Should().Contain("PUT /api/admin/secrets/billing/Billing%3ASecretKey");
        _vault.Requests.Should().NotContain(request => request.Contains("DO_NOT_LOG"));
        _vault.LastBody.Should().Contain("sk_live_DO_NOT_LOG");
    }

    [Fact]
    public async Task Vault_validation_messages_become_validation_errors()
    {
        _vault.NextAdminResponse = (HttpStatusCode.BadRequest,
            """{"title":"Invalid","errors":{"Name":["Name must be 200 characters or fewer."]}}""");

        var result = await Gateway().ApplyAsync("pilot-vault", new TokenMintChange { Namespace = "billing", Name = "x" }, default);

        var error = Error(result);
        error.Type.Should().Be(AppErrorType.Validation);
        error.Message.Should().Be("Name must be 200 characters or fewer.");
    }

    [Fact]
    public async Task A_rejected_credential_is_unavailable_not_unauthorized()
    {
        _vault.RejectSignIn = true;

        var error = Error(await Gateway().ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default));

        error.Type.Should().Be(AppErrorType.ExternalUnavailable);
        error.Message.Should().Contain("administrator credential");
    }

    [Fact]
    public async Task Unknown_vaults_are_refused_without_a_request()
    {
        var error = Error(await Gateway().ReadAsync(VaultResource.Namespaces, "elsewhere", null, default));

        error.Type.Should().Be(AppErrorType.NotFound);
        _vault.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_credential_is_reported_without_contacting_the_vault()
    {
        File.Delete(Path.Combine(_root, "vaults", "pilot-vault", "password"));

        var error = Error(await Gateway().ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default));

        error.Message.Should().Contain("no administrator credential");
        _vault.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreachable_vault_is_unavailable()
    {
        _vault.Unreachable = true;

        var error = Error(await Gateway().ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default));

        error.Type.Should().Be(AppErrorType.ExternalUnavailable);
    }

    [Fact]
    public async Task Listing_reports_each_vaults_sealed_state()
    {
        var result = await Gateway().ReadAsync(VaultResource.Vaults, null, null, default);

        var vault = ((AppResult<JsonElement>.Success)result).Data[0];
        vault.GetProperty("id").GetString().Should().Be("pilot-vault");
        vault.GetProperty("status").GetString().Should().Be("unsealed");
        vault.TryGetProperty("url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task An_expired_session_is_not_reused()
    {
        var cache = new VaultSessionCache(_time);
        var gateway = Gateway(cache);
        await gateway.ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default);

        _time.Advance(TimeSpan.FromMinutes(59));
        await gateway.ReadAsync(VaultResource.Namespaces, "pilot-vault", null, default);

        _vault.SignIns.Should().Be(2);
    }

    private static AppError Error(AppResult<JsonElement> result) => ((AppResult<JsonElement>.Failure)result).Error;

    // ---- Doubles ----

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedVault : HttpMessageHandler
    {
        public int SignIns { get; private set; }
        public List<string> Requests { get; } = [];
        public string LastBody { get; private set; } = "";
        public bool RejectNextAdminCall { get; set; }
        public bool RejectSignIn { get; set; }
        public bool Unreachable { get; set; }
        public (HttpStatusCode Status, string Body)? NextAdminResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Unreachable)
                throw new HttpRequestException("connection refused");
            var path = request.RequestUri!.PathAndQuery;
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            if (path == "/api/system/status")
                return Json(HttpStatusCode.OK, """{"isSealed":false}""");
            Requests.Add(request.Method + " " + request.RequestUri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped));
            if (path == "/api/identity/sign-in")
            {
                if (RejectSignIn)
                    return Json(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
                SignIns++;
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
                    new { data = new { token = "session-" + SignIns, expiresAtUtc = "2026-09-25T13:00:00Z" } }));
            }
            if (request.Headers.Authorization?.Parameter is null)
                return Json(HttpStatusCode.Unauthorized, "{}");
            if (RejectNextAdminCall)
            {
                RejectNextAdminCall = false;
                return Json(HttpStatusCode.Unauthorized, "{}");
            }
            if (NextAdminResponse is { } scripted)
            {
                NextAdminResponse = null;
                return Json(scripted.Status, scripted.Body);
            }
            return Json(HttpStatusCode.OK, """{"data":[]}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
