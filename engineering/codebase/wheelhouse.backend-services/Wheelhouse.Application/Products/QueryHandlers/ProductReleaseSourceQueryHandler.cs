using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductReleaseSourceQuery"/>: the release source a save of the product with this primary
/// repository would store (<see cref="ProductReleaseDerivation"/>). A product that exists reads its further
/// repositories too, so the note says what a primary repository's missing descriptor holds back, and while its descriptor
/// cannot be read the answer shows the images it keeps, as a save would. A repository GitHub cannot serve is an answer
/// with its note, not a failure.</summary>
public sealed class ProductReleaseSourceQueryHandler(IProductsRepository products, CatalogReleaseService releases)
    : IQueryHandler<ProductReleaseSourceQuery, AppResult<ProductReleaseSourceDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductReleaseSourceDto>> HandleAsync(
        ProductReleaseSourceQuery request, CancellationToken cancellationToken)
    {
        var existing = await products.GetBySlugAsync(request.Slug, cancellationToken);
        IReadOnlyList<string> repositories =
        [
            request.Repository,
            .. (existing?.Components ?? [])
                .Select(component => component.Repository)
                .Where(repository => !string.Equals(repository, request.Repository, StringComparison.OrdinalIgnoreCase)),
        ];
        var derived = (await ProductReleaseDerivation.DeriveAsync(
            releases, request.Slug, repositories, request.Branch, request.Refresh, cancellationToken))[0];
        var kept = derived.FromDescriptor || existing is null
            ? null
            : ProductRepositories.Of(existing)
                .FirstOrDefault(item => string.Equals(item.Repository, request.Repository, StringComparison.OrdinalIgnoreCase))
                .Release?.Images;
        var keeps = kept is { Count: > 0 };
        return AppResult<ProductReleaseSourceDto>.Ok(new ProductReleaseSourceDto
        {
            Repository = request.Repository,
            Branch = request.Branch,
            Asset = derived.Release.Asset,
            Workflow = derived.Release.Workflow,
            Images = [.. (keeps ? kept! : derived.Release.Images).Select(image => new ReleaseImageDto { Service = image.Service, Image = image.Image })],
            FromDescriptor = derived.FromDescriptor,
            Note = keeps
                ? $"deploy.yml in {request.Repository}@{request.Branch} cannot be read or used now, so a save keeps the product's images until it can."
                : derived.Note,
        });
    }
}
