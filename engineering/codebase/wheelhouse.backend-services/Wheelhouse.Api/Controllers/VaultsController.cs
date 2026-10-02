using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Wheelhouse.Api.Filters;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Vaults.Commands;
using Wheelhouse.Application.Vaults.Models;
using Wheelhouse.Application.Vaults.Queries;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using Wheelhouse.Application.Vaults.Hygiene;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;
using WoW.Two.Sdk.Backend.Beta.Web.Contracts;
using WoW.Two.Sdk.Backend.Beta.Web.ErrorMapping;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the secrets vaults Wheelhouse administers over HTTP; values are write-only and never returned.</summary>
[ApiController]
[Route("api/vaults")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VaultsController(ISender sender, IErrorHttpStatusCodeMapper errors) : ControllerBase
{
    /// <summary>Lists the configured vaults with their sealed state.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Render(await sender.SendAsync(new VaultReadQuery(VaultResource.Vaults), ct));

    /// <summary>Lists a vault's namespaces.</summary>
    [HttpGet("{vault}/namespaces")]
    public async Task<IActionResult> Namespaces([RegularExpression(VaultRules.Vault)] string vault, CancellationToken ct) =>
        Render(await sender.SendAsync(new VaultReadQuery(VaultResource.Namespaces, vault), ct));

    /// <summary>Summarizes which secrets and product tokens are due for rotation; metadata only.</summary>
    [HttpGet("{vault}/hygiene")]
    public async Task<IActionResult> Hygiene([RegularExpression(VaultRules.Vault)] string vault, CancellationToken ct) =>
        (await sender.SendAsync(new VaultHygieneQuery(vault), ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<VaultHygiene>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));

    /// <summary>Creates a namespace.</summary>
    [HttpPost("{vault}/namespaces")]
    [RequireAction("vault")]
    public Task<IActionResult> CreateNamespace(
        [RegularExpression(VaultRules.Vault)] string vault, VaultNamespaceCreateRequest request, CancellationToken ct) =>
        Change(vault, new NamespaceCreateChange { Namespace = request.Slug, Name = request.Name }, ct);

    /// <summary>Lists a namespace's secret metadata; values are never included.</summary>
    [HttpGet("{vault}/secrets")]
    public async Task<IActionResult> Secrets(
        [RegularExpression(VaultRules.Vault)] string vault,
        [FromQuery, Required, RegularExpression(VaultRules.Namespace)] string ns, CancellationToken ct) =>
        Render(await sender.SendAsync(new VaultReadQuery(VaultResource.Secrets, vault, ns), ct));

    /// <summary>Writes a new secret version.</summary>
    [HttpPut("{vault}/secrets/{ns}/{key}")]
    [RequireAction("vault")]
    public Task<IActionResult> SetSecret(
        [RegularExpression(VaultRules.Vault)] string vault, [RegularExpression(VaultRules.Namespace)] string ns,
        [RegularExpression(VaultRules.Key)] string key, VaultSecretSetRequest request, CancellationToken ct) =>
        Change(vault, new SecretSetChange { Namespace = ns, Key = key, Value = request.Value, Description = request.Description }, ct);

    /// <summary>Disables or re-enables a secret.</summary>
    [HttpPost("{vault}/secrets/{ns}/{key}/state")]
    [RequireAction("vault")]
    public Task<IActionResult> SetSecretState(
        [RegularExpression(VaultRules.Vault)] string vault, [RegularExpression(VaultRules.Namespace)] string ns,
        [RegularExpression(VaultRules.Key)] string key, VaultSecretStateRequest request, CancellationToken ct) =>
        Change(vault, new SecretStateChange { Namespace = ns, Key = key, Disabled = request.Disabled!.Value }, ct);

    /// <summary>Lists a namespace's product tokens.</summary>
    [HttpGet("{vault}/namespaces/{ns}/tokens")]
    public async Task<IActionResult> Tokens(
        [RegularExpression(VaultRules.Vault)] string vault, [RegularExpression(VaultRules.Namespace)] string ns, CancellationToken ct) =>
        Render(await sender.SendAsync(new VaultReadQuery(VaultResource.Tokens, vault, ns), ct));

    /// <summary>Mints a product token; the response is the only time the token is shown.</summary>
    [HttpPost("{vault}/namespaces/{ns}/tokens")]
    [RequireAction("vault")]
    public Task<IActionResult> MintToken(
        [RegularExpression(VaultRules.Vault)] string vault, [RegularExpression(VaultRules.Namespace)] string ns,
        VaultTokenMintRequest request, CancellationToken ct) =>
        Change(vault, new TokenMintChange { Namespace = ns, Name = request.Name }, ct);

    /// <summary>Revokes a product token.</summary>
    [HttpPost("{vault}/namespaces/{ns}/tokens/{tokenId:guid}/revoke")]
    [RequireAction("vault")]
    public Task<IActionResult> RevokeToken(
        [RegularExpression(VaultRules.Vault)] string vault, [RegularExpression(VaultRules.Namespace)] string ns,
        Guid tokenId, CancellationToken ct) =>
        Change(vault, new TokenRevokeChange { Namespace = ns, TokenId = tokenId }, ct);

    /// <summary>Gets every vault's definition, endpoint included, for the inventory editor.</summary>
    [HttpGet("definitions")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<VaultDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Definitions(CancellationToken ct) =>
        (await sender.SendAsync(new VaultGetAllQuery(), ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<IReadOnlyList<VaultDto>>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));

    /// <summary>Adds a vault; it opens nothing until its administrator password is on the control host.</summary>
    [HttpPost]
    [RequireAction("vault")]
    [ProducesResponseType<ApiResponse<VaultDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateVaultApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new VaultCreateCommand
        {
            Slug = request.Slug, Name = request.Name, Server = request.Server, Url = request.Url,
        }, ct)).Match<IActionResult>(
            ok => StatusCode(StatusCodes.Status201Created, ApiResponse<VaultDto>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));

    /// <summary>Changes a vault's definition; its slug stays.</summary>
    [HttpPut("{vault}")]
    [RequireAction("vault")]
    [ProducesResponseType<ApiResponse<VaultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        [RegularExpression(VaultRules.Vault)] string vault, UpdateVaultApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new VaultUpdateCommand
        {
            Slug = vault, Name = request.Name, Server = request.Server, Url = request.Url,
        }, ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<VaultDto>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));

    /// <summary>Stops administering a vault; the vault and its secrets stay where they run.</summary>
    [HttpDelete("{vault}")]
    [RequireAction("vault")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete([RegularExpression(VaultRules.Vault)] string vault, CancellationToken ct) =>
        (await sender.SendAsync(new VaultDeleteCommand { Slug = vault }, ct)).Match<IActionResult>(
            _ => NoContent(),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));

    private async Task<IActionResult> Change(string vault, VaultChange change, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirst("wt:username")?.Value ?? "authenticated-admin";
        return Render(await sender.SendAsync(new VaultChangeCommand(vault, change, actor), ct));
    }

    private IActionResult Render(AppResult<JsonElement> result) => result.Match<IActionResult>(
        ok => Ok(ApiResponse<JsonElement>.Ok(ok.Data)),
        fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));
}
