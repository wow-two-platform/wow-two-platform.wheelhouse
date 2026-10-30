using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

namespace Wheelhouse.Application.Audit;

/// <summary>Reads one page of audit entries, newest first, optionally about one product.</summary>
/// <param name="PageToken">The previous page's <c>nextPageToken</c>, an SDK page token; null for the newest entries.</param>
/// <param name="PageSize">How many entries to return, 1-200.</param>
/// <param name="Product">Only entries about this product, by slug: the product, its services, its targets and its
/// vault namespaces. A product Wheelhouse no longer holds still matches its own slug and conventional names.</param>
public sealed record AuditListQuery(string? PageToken, int PageSize, string? Product = null)
    : IQuery<AppResult<TokenPageDto<AuditEntryDto>>>
{
    /// <summary>The page size when a request names none.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The keys a page token carries: the last entry's sequence number.</summary>
    public static readonly IReadOnlyList<Type> PageTokenKeys = [typeof(long)];
}
