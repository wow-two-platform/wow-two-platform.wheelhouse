using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Queries;

/// <summary>Reads what a product's <c>deploy.yml</c> declares at its branch head.</summary>
/// <param name="Slug">The product's slug.</param>
public sealed record ProductDescriptorQuery(string Slug) : IQuery<AppResult<ProductDescriptorDto>>;
