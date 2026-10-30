using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the setting and secret keys an owner adds to a product beside those its releases declare. The
/// settings plan lists them with their values per environment.</summary>
[ApiController]
[Route("api/products/{product}/setting-keys")]
[Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProductSettingKeysController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Adds a key, or changes one the owner added; a key that reads as a secret needs <c>confirmPlain</c> to be
    /// a plain setting.</summary>
    [HttpPut("{key}")]
    [RequireActionHeader("settings")]
    [ProducesResponseType<ApiResponse<DeploySettingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        [RegularExpression(Slug)] string product, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, ProductSettingKeyRequest request,
        CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductSettingKeySetCommand(product, key, request.Secret, request.Type,
            request.Format, request.Choices, request.Required, request.Note, request.ConfirmPlain, actor.Actor), ct));

    /// <summary>Removes a key the owner added.</summary>
    [HttpDelete("{key}")]
    [RequireActionHeader("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(
        [RegularExpression(Slug)] string product, [RegularExpression(FormatPatternConstants.ConfigurationKey)] string key, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductSettingKeyRemoveCommand(product, key), ct), _ => NoContent());
}
