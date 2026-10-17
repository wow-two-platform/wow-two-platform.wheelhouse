using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Audit.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>The append-only, hash-chained record of operator actions.</summary>
public interface IAuditTrail
{
    /// <summary>Appends one finished action, chained to the entry before it.</summary>
    Task AppendAsync(AuditRecord record, CancellationToken ct = default);

    /// <summary>Lists entries newest first, up to <paramref name="limit"/>, before sequence <paramref name="before"/> when
    /// given, and only those about one product when <paramref name="product"/> is given.</summary>
    Task<IReadOnlyList<AuditEntry>> ListAsync(
        int limit, long? before, AuditProductScope? product = null, CancellationToken ct = default);

    /// <summary>Recomputes the whole chain and reports whether every entry and link is intact.</summary>
    Task<AuditVerification> VerifyAsync(CancellationToken ct = default);
}
