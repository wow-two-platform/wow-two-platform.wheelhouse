using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes what an environment's settings and secrets do after a save: applying them now or at a set time, a
/// key's link to another product's key, and each secret's versions, restore, regeneration and expiry. Signed-in owner
/// only; values never travel back.</summary>
[ApiController]
[Route("api/deployments/targets/{target}")]
[Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TargetSettingsController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Applies the environment's edited settings and secrets: now, as a deploy of the release the target runs
    /// (only services whose settings changed restart), or at <c>at</c>, replacing an apply already scheduled.</summary>
    [HttpPost("settings/apply")]
    [RequireActionHeader("settings")]
    [ProducesResponseType<ApiResponse<SettingsApplyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply([RegularExpression(Slug)] string target, SettingsApplyRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(
            new SettingsApplyCommand(target, request.At, actor.Actor, request.Reason, request.Confirm), ct));

    /// <summary>Drops the target's scheduled apply.</summary>
    [HttpDelete("settings/apply")]
    [RequireActionHeader("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelApply([RegularExpression(Slug)] string target, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new SettingsApplyCancelCommand(target), ct), _ => NoContent());

    /// <summary>Links a setting key of the target's product environment to another product's key in the same
    /// environment: each deploy renders the source's current value under the key, over a value of its own. Replaces the
    /// key's earlier link; audited by both keys, never a value.</summary>
    [HttpPut("settings/{key}/link")]
    [RequireActionHeader("settings")]
    [ProducesResponseType<ApiResponse<SettingLinkDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Link(
        [RegularExpression(Slug)] string target, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, SettingLinkRequest request,
        CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(
            new SettingLinkSetCommand(target, key, request.SourceTarget, request.SourceKey, actor.Actor), ct));

    /// <summary>Removes a key's link; the key renders its own value again, and the source keeps everything it holds.</summary>
    [HttpDelete("settings/{key}/link")]
    [RequireActionHeader("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlink(
        [RegularExpression(Slug)] string target, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new SettingLinkRemoveCommand(target, key), ct), _ => NoContent());

    /// <summary>Gets a secret's versions: who, when, fingerprint and note; never a value.</summary>
    [HttpGet("secrets/{key}/versions")]
    [ProducesResponseType<ApiResponse<SecretHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Versions(
        [RegularExpression(Slug)] string target, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new SecretHistoryQuery(target, key), ct));

    /// <summary>Restores an earlier version of a secret as its newest version.</summary>
    [HttpPost("secrets/{key}/restore")]
    [RequireActionHeader("settings")]
    [ProducesResponseType<ApiResponse<SecretWritten>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(
        [RegularExpression(Slug)] string target, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, SecretRestoreRequest request,
        CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new SecretRestoreCommand(target, key, request.Version, request.Note, actor.Actor), ct));

    /// <summary>Generates a new value for a secret the release has Wheelhouse generate, as its newest version; the next
    /// deploy or apply renders it. The value never travels back: the reply names the version and fingerprint.</summary>
    [HttpPost("secrets/{key}/regenerate")]
    [RequireActionHeader("settings")]
    [ProducesResponseType<ApiResponse<SecretWritten>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Regenerate(
        [RegularExpression(Slug)] string target, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SecretRegenerateRequest? request,
        CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(
            new SecretRegenerateCommand(target, key, actor.Actor, request?.Note, request?.Release), ct));

    /// <summary>Sets or clears a secret's expiry.</summary>
    [HttpPut("secrets/{key}/expiry")]
    [RequireActionHeader("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Expiry(
        [RegularExpression(Slug)] string target, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, SecretExpiryRequest request,
        CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new SecretExpiryCommand(target, key, request.ExpiresAt), ct), _ => NoContent());
}
