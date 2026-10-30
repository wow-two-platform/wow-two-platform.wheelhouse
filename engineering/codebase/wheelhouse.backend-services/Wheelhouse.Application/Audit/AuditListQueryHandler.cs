using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

namespace Wheelhouse.Application.Audit;

/// <summary>Handles <see cref="AuditListQuery"/>.</summary>
public sealed class AuditListQueryHandler(
    IAuditTrail trail, IProductsRepository products, ITargetsRepository targets, IVaultsRepository vaults)
    : IQueryHandler<AuditListQuery, AppResult<TokenPageDto<AuditEntryDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<TokenPageDto<AuditEntryDto>>> HandleAsync(
        AuditListQuery request, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Product)
            ? null
            : await ScopeOfAsync(request.Product.Trim(), cancellationToken);
        var page = await trail.ListAsync(
            new TokenPageApiRequest { PageToken = request.PageToken, PageSize = request.PageSize }, scope, cancellationToken);
        return AppResult<TokenPageDto<AuditEntryDto>>.Ok(new TokenPageDto<AuditEntryDto>
        {
            Items = [.. page.Items.Select(AuditEntryDto.From)],
            PageSize = page.PageSize,
            NextPageToken = page.NextPageToken
        });
    }

    /// <summary>Names every subject the product's entries carry: its current targets and, so a removed target's history
    /// stays, the conventional <c>product-environment</c> names; and its namespace in each vault, named
    /// <c>product-environment</c> as the product projection names it.</summary>
    private async Task<AuditProductScope> ScopeOfAsync(string slug, CancellationToken ct)
    {
        var environments = Enum.GetValues<DeploymentEnvironment>()
            .Select(environment => slug + "-" + environment.ToString().ToLowerInvariant())
            .ToArray();
        var product = await products.GetBySlugAsync(slug, ct);
        var own = product is null
            ? []
            : (await targets.GetAllAsync(ct)).Where(target => target.ProductId == product.Id).Select(target => target.Slug);
        var namespaces = (await vaults.GetAllAsync(ct))
            .SelectMany(vault => environments.Select(name => vault.Slug + "/" + name));
        return new AuditProductScope(slug, [.. own.Concat(environments).Distinct(StringComparer.Ordinal)], [.. namespaces]);
    }
}
