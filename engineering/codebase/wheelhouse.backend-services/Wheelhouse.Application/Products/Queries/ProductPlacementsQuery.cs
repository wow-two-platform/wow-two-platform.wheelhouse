using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Queries;

/// <summary>Reads a product's placements on servers, newest first, with their live steps.</summary>
/// <param name="Slug">The product's slug.</param>
public sealed record ProductPlacementsQuery(string Slug) : IQuery<AppResult<JsonElement>>;
