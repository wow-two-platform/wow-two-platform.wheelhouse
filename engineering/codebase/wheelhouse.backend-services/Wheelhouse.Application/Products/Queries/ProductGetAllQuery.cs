using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Queries;

/// <summary>Represents a query to get every catalog product.</summary>
public sealed record ProductGetAllQuery : IQuery<AppResult<IReadOnlyList<ProductDto>>>;
