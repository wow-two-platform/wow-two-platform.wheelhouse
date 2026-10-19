using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the built-in Secrets Vault: its state, its issues, the full decryption check and the import from a
/// standalone vault. Signed-in owner only — an integration key gets 403 and MCP never reaches it. No response carries a
/// value or a key.</summary>
[ApiController]
[Route("api/vault")]
[Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SecretsVaultController(ISender sender) : ControllerBase
{
    /// <summary>Gets the vault's state: sealed or not and why, the root key file's path, and what it holds.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<VaultStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new VaultStatusQuery(), ct));

    /// <summary>Gets what needs the owner: a sealed vault holding secrets, and secrets lapsing within 30 days or lapsed.</summary>
    [HttpGet("issues")]
    [ProducesResponseType<ApiResponse<VaultIssuesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Issues(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new VaultIssuesQuery(), ct));

    /// <summary>Decrypts everything the vault holds and counts what decrypted: the proof after a restore.</summary>
    [HttpPost("verify")]
    [RequireActionHeader("vault")]
    [ProducesResponseType<ApiResponse<VaultVerificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new VaultVerifyCommand(), ct));

    /// <summary>Imports an inventory vault's current values over its management API: each namespace named after a product
    /// environment, re-encrypted under the product's key and verified; counts only.</summary>
    [HttpPost("import")]
    [RequireActionHeader("vault")]
    [ProducesResponseType<ApiResponse<VaultImportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(VaultImportRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new VaultImportCommand(request.Vault), ct));
}
