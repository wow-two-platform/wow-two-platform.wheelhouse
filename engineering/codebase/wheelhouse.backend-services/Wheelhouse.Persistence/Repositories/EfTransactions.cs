using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ITransactions"/> on the request's context.</summary>
internal sealed class EfTransactions(WheelhouseDbContext db) : ITransactions
{
    /// <inheritdoc />
    public async Task<ITransactionScope> BeginAsync(CancellationToken ct) =>
        db.Database.CurrentTransaction is not null
            ? new Joined()
            : new Owned(db, await db.Database.BeginTransactionAsync(ct));

    /// <summary>A transaction this scope began: a dispose without a commit rolls back and forgets what was staged, so
    /// a later save on the same context (the audit trail's) cannot write it.</summary>
    private sealed class Owned(WheelhouseDbContext db, IDbContextTransaction transaction) : ITransactionScope
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken ct)
        {
            await transaction.CommitAsync(ct);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
                db.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }
    }

    /// <summary>A scope inside a transaction already running; the outer one commits.</summary>
    private sealed class Joined : ITransactionScope
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
