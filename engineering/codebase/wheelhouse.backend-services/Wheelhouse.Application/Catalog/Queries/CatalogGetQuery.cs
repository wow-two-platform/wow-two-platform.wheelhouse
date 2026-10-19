using Wheelhouse.Application.Catalog.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Catalog.Queries;

/// <summary>Represents a query to read every hub's catalog, merged, with the product each entry already is.</summary>
public sealed record CatalogGetQuery : IQuery<AppResult<CatalogDto>>;
