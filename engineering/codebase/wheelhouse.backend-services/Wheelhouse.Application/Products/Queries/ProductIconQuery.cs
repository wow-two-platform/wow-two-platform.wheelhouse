using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.Queries;

/// <summary>Represents a query to get the icon a catalog product's repository carries for a scope, theme, form and
/// color.</summary>
public sealed record ProductIconQuery : IQuery<AppResult<ProductIconImage>>
{
    /// <summary>Gets the product's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets which icon is wanted.</summary>
    public ProductIconRequest Icon { get; init; } = new();
}
