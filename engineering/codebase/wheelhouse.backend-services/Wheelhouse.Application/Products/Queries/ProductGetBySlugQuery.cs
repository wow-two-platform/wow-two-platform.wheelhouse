using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Queries;

/// <summary>Represents a query to get one catalog product by its slug.</summary>
public sealed record ProductGetBySlugQuery : IQuery<AppResult<ProductDto>>
{
    /// <summary>Gets the product's slug.</summary>
    public required string Slug { get; init; }
}
