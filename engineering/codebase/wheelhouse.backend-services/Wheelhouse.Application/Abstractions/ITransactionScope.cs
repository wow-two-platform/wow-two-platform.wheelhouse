namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents an open transaction.</summary>
public interface ITransactionScope : IAsyncDisposable
{
    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken ct);
}
