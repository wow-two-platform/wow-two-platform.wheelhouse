using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Audit.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Locks.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit.Validators;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IAuditTrail"/>: one transaction per append, serialized so each entry
/// chains to the one committed before it.</summary>
internal sealed class EfAuditTrail(
    WheelhouseDbContext db, IHashChainSealer<AuditEntry> sealer, IHashChainValidator<AuditEntry> validator, TimeProvider time)
    : IAuditTrail
{
    // Every append takes this one transaction lock, so the next entry reads the head the previous one committed.
    private const string AppendLockKey = "audit:append";

    /// <inheritdoc />
    public async Task AppendAsync(AuditRecord record, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.AcquireTransactionLocksAsync([AppendLockKey], ct);
        var last = await db.AuditEntries.AsNoTracking().OrderByDescending(e => e.Sequence).FirstOrDefaultAsync(ct);
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            // Postgres keeps microseconds; the SDK hashes timestamps at that precision, so the stored row verifies.
            OccurredAtUtc = time.GetUtcNow(),
            Actor = record.Actor,
            Action = record.Action,
            Subject = record.Subject,
            Outcome = record.Outcome,
            Detail = record.Detail,
            Reason = record.Reason
        };
        sealer.Seal(entry, last);
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> ListAsync(
        int limit, long? before, AuditProductScope? product = null, CancellationToken ct = default)
    {
        var entries = db.AuditEntries.AsNoTracking().Where(e => before == null || e.Sequence < before);
        if (product is not null)
        {
            // Each action family matches only its own subjects, so a server or vault sharing the product's slug stays out.
            var slug = product.Product;
            var targets = product.Targets.ToArray();
            var namespaces = product.Namespaces.ToArray();
            var services = product.ServicePrefix;
            entries = entries.Where(e =>
                ((e.Action.StartsWith("product.") || e.Action.StartsWith("build.")) && e.Subject == slug)
                || ((e.Action.StartsWith("deployment.") || e.Action.StartsWith("target.")) && targets.Contains(e.Subject))
                || (e.Action.StartsWith("vault.") && namespaces.Contains(e.Subject))
                || (e.Action.StartsWith("service.") && e.Subject.StartsWith(services)));
        }

        return await entries.OrderByDescending(e => e.Sequence).Take(limit).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AuditVerification> VerifyAsync(CancellationToken ct = default)
    {
        var entries = await db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync(ct);
        var result = validator.Validate(entries);
        return result.IsIntact
            ? new AuditVerification(true, entries.Count, null, null)
            : new AuditVerification(false, entries.Count, result.BrokenSequence, result.Reason.ToString());
    }
}
