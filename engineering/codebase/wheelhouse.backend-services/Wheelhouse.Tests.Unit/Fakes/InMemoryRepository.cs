using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Keeps entities in a list behind the SDK repository contract, for tests that need no database.</summary>
/// <typeparam name="TEntity">The stored entity.</typeparam>
public class InMemoryRepository<TEntity> : IRepository<TEntity, Guid> where TEntity : class, IKeyedEntity<Guid>
{
    /// <summary>Gets the stored entities.</summary>
    public List<TEntity> Rows { get; } = [];

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    /// <inheritdoc />
    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TEntity>>([.. Rows]);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Any(row => row.Id == id));

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rows.Count);

    /// <inheritdoc />
    public Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Rows.Add(entity);
        return Task.FromResult(entity);
    }

    /// <inheritdoc />
    public Task CreateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Rows.AddRange(entities);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Rows.Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.RemoveAll(row => row.Id == id) > 0);
}
