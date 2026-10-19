using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Constants;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Settings.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Handles <see cref="ProductSettingKeySetCommand"/>: a key that reads as a secret is refused as a plain setting
/// unless the owner confirms it.</summary>
public sealed class ProductSettingKeySetCommandHandler(IProductsRepository products, IProductSettingKeysRepository keys)
    : ICommandHandler<ProductSettingKeySetCommand, AppResult<DeploySettingDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploySettingDto>> HandleAsync(ProductSettingKeySetCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Product, cancellationToken);
        if (product is null)
            return AppResult<DeploySettingDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
        if (!request.Secret && !request.ConfirmPlain && SettingTypes.LooksSecret(request.Key))
            return AppResult<DeploySettingDto>.Fail(AppErrorFactory.Validation(
                request.Key + " reads as a secret; add it as a secret, or confirm it is a plain setting."));
        var type = SettingTypes.Parse(request.Type) ?? SettingType.Text;
        var choices = type == SettingType.Choice ? request.Choices?.Select(choice => choice.Trim()).Distinct(StringComparer.Ordinal).ToList() : null;
        await keys.SetAsync(new ProductSettingKeyEntity
        {
            ProductId = product.Id,
            Key = request.Key,
            Secret = request.Secret,
            Type = type,
            Format = type == SettingType.Date ? request.Format ?? SettingTypes.DefaultDateFormat : null,
            Choices = choices is null ? null : string.Join('\n', choices),
            Required = request.Required,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedBy = request.Actor,
        }, cancellationToken);
        return AppResult<DeploySettingDto>.Ok(new DeploySettingDto(request.Key, request.Required, request.Secret,
            request.Required ? SettingSources.Missing : SettingSources.Unset, Allowed: choices)
        {
            Type = SettingTypes.NameOf(type),
            Format = type == SettingType.Date ? request.Format ?? SettingTypes.DefaultDateFormat : null,
            AdHoc = true,
        });
    }
}
