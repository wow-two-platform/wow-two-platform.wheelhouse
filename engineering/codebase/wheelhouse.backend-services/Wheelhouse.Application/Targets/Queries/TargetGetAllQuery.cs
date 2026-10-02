using Wheelhouse.Application.Targets.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.Queries;

/// <summary>Represents a query to get every target, or one product's.</summary>
public sealed record TargetGetAllQuery : IQuery<AppResult<IReadOnlyList<TargetDto>>>
{
    /// <summary>Gets the slug of the product whose targets to get, or <c>null</c> for every product's.</summary>
    public string? Product { get; init; }
}
