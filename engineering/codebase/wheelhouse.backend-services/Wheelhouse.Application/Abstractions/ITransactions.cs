namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines a database transaction around several stores' writes: they commit together or not at all.</summary>
public interface ITransactions
{
    /// <summary>Begins a transaction; inside one already running, joins it and leaves the commit to it.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The transaction; disposing it without a commit rolls back and forgets every staged change.</returns>
    Task<ITransactionScope> BeginAsync(CancellationToken ct);
}
