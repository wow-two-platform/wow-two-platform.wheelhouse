using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Domain.Settings.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Handles <see cref="SettingLinkSetCommand"/>. Refuses a source target that does not exist, belongs to the
/// same product (a release's <c>from</c> links keys inside one) or to another environment; a key the release (else the one
/// the target runs) links with <c>from</c>; and a second hop: a source key that is itself linked, or a key another link
/// takes as its source. The source need not hold a value yet: the key renders once it does.</summary>
public sealed class SettingLinkSetCommandHandler(
    ITargetsRepository targets,
    IProductsRepository products,
    ISettingLinksRepository links,
    DeploymentSettingsService settings)
    : ICommandHandler<SettingLinkSetCommand, AppResult<SettingLinkDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<SettingLinkDto>> HandleAsync(SettingLinkSetCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        var product = target is null ? null : await products.GetByIdAsync(target.ProductId, cancellationToken);
        if (target is null || product is null)
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        var source = await targets.GetBySlugAsync(request.SourceTarget, cancellationToken);
        var owner = source is null ? null : await products.GetByIdAsync(source.ProductId, cancellationToken);
        if (source is null || owner is null)
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.Validation($"Source target '{request.SourceTarget}' was not found."));
        if (owner.Id == product.Id)
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.Validation(
                $"{request.SourceTarget} belongs to {product.Slug} too; a link reaches another product's key, and a release's from links keys inside one."));
        if (source.Environment != target.Environment)
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.Validation(
                $"{request.SourceTarget} is a {DeploymentSettingsService.EnvironmentName(source.Environment)} target and {request.Target} a "
                + $"{DeploymentSettingsService.EnvironmentName(target.Environment)} one; a link stays inside one environment."));

        if ((await settings.DeclarationsAsync(target, product, null, cancellationToken)).GetValueOrDefault(request.Key)?.From is { } from)
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.Conflict(
                $"The release links {request.Key} to {from}; it takes that key's value, so it links nowhere else."));
        if ((await links.GetAsync(owner.Id, source.Environment, cancellationToken)).FirstOrDefault(link => link.Key == request.SourceKey) is { } hop)
        {
            var next = await targets.GetByIdAsync(hop.SourceTargetId, cancellationToken);
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.Conflict(
                $"{request.SourceTarget} {request.SourceKey} is itself linked to {next?.Slug ?? "another target"} {hop.SourceKey}; "
                + "link to that key instead, since links go one hop."));
        }
        if ((await links.GetBySourceAsync(product.Id, target.Environment, request.Key, cancellationToken)).FirstOrDefault() is { } sourcing)
        {
            var by = await products.GetByIdAsync(sourcing.ProductId, cancellationToken);
            return AppResult<SettingLinkDto>.Fail(AppErrorFactory.Conflict(
                $"{request.Key} is the source of {by?.Slug ?? "another product"}'s {sourcing.Key}; links go one hop, so it links nowhere."));
        }

        await links.SetAsync(new SettingLinkEntity
        {
            ProductId = product.Id,
            Environment = target.Environment,
            Key = request.Key,
            SourceTargetId = source.Id,
            SourceKey = request.SourceKey,
            CreatedBy = request.Actor,
        }, cancellationToken);
        return AppResult<SettingLinkDto>.Ok(new SettingLinkDto(source.Slug, request.SourceKey));
    }
}
