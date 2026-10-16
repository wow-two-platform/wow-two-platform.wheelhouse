using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Audit;

/// <summary>Reads the newest audit entries, optionally those before one sequence number.</summary>
/// <param name="Limit">How many entries to return, 1-200.</param>
/// <param name="Before">Only entries with a lower sequence number, to page back.</param>
public sealed record AuditListQuery(int Limit, long? Before) : IQuery<AppResult<IReadOnlyList<AuditEntryDto>>>;
