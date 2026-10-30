using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Audit.Entities;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

namespace Wheelhouse.Application.Abstractions;

/// <summary>The append-only, hash-chained record of actions.</summary>
public interface IAuditTrail
{
    /// <summary>Appends one finished action, chained to the entry before it.</summary>
    Task AppendAsync(AuditRecord record, CancellationToken ct = default);

    /// <summary>Lists one page of entries newest first, after the sequence number <paramref name="page"/>'s token
    /// carries, and only those about one product when <paramref name="product"/> is given.</summary>
    Task<TokenPageDto<AuditEntry>> ListAsync(
        TokenPageApiRequest page, AuditProductScope? product = null, CancellationToken ct = default);

    /// <summary>Recomputes the whole chain and reports whether every entry and link is intact.</summary>
    Task<AuditVerification> VerifyAsync(CancellationToken ct = default);
}
