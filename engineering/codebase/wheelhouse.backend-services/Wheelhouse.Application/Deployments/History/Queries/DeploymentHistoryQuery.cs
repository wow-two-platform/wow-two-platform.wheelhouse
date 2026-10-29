using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Reads one page of matching deployments, newest first.</summary>
/// <param name="Filter">Which deployments match.</param>
/// <param name="Cursor">The previous page's cursor, an SDK page token; null for the first page.</param>
/// <param name="Limit">The page size, 1-100.</param>
public sealed record DeploymentHistoryQuery(DeploymentFilter Filter, string? Cursor, int Limit)
    : IQuery<AppResult<DeploymentPageDto>>
{
    /// <summary>The largest page.</summary>
    public const int MaxLimit = 100;

    /// <summary>The keys a cursor carries: the last entry's submission ticks and identifier.</summary>
    public static readonly IReadOnlyList<Type> CursorKeys = [typeof(long), typeof(string)];
}
