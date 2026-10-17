using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Audit;

/// <summary>Reads the newest audit entries, optionally those before one sequence number, optionally about one product.</summary>
/// <param name="Limit">How many entries to return, 1-200.</param>
/// <param name="Before">Only entries with a lower sequence number, to page back.</param>
/// <param name="Product">Only entries about this product, by slug: the product, its services, its targets and its
/// vault namespaces. A product Wheelhouse no longer holds still matches its own slug and conventional names.</param>
public sealed record AuditListQuery(int Limit, long? Before, string? Product = null)
    : IQuery<AppResult<IReadOnlyList<AuditEntryDto>>>;
