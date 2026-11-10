using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Handles <see cref="SettingLinkRemoveCommand"/>: drops the link alone; neither the key's own value nor the
/// source's changes.</summary>
public sealed class SettingLinkRemoveCommandHandler(
    ITargetsRepository targets,
    IProductsRepository products,
    ISettingLinksRepository links)
    : ICommandHandler<SettingLinkRemoveCommand, AppResult<bool>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<bool>> HandleAsync(SettingLinkRemoveCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        var product = target is null ? null : await products.GetByIdAsync(target.ProductId, cancellationToken);
        if (target is null || product is null)
            return AppResult<bool>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        var link = (await links.GetAsync(product.Id, target.Environment, cancellationToken)).FirstOrDefault(item => item.Key == request.Key);
        if (link is null || !await links.RemoveAsync(product.Id, target.Environment, request.Key, cancellationToken))
            return AppResult<bool>.Fail(AppErrorFactory.NotFound($"{request.Key} on {request.Target} links to no other product's key."));
        request.Outcome.Detail = ((await targets.GetByIdAsync(link.SourceTargetId, cancellationToken))?.Slug ?? "a removed target")
            + "/" + link.SourceKey;
        return AppResult<bool>.Ok(true);
    }
}
