using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Queries;

/// <summary>Derives the release source a product's repository gives, for a product being added or edited.</summary>
public sealed record ProductReleaseSourceQuery : IQuery<AppResult<ProductReleaseSourceDto>>
{
    /// <summary>Gets the product's slug; the release asset is named after it.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the branch whose <c>deploy.yml</c> is read.</summary>
    public required string Branch { get; init; }

    /// <summary>Gets whether to ask GitHub for the branch head now rather than take one read a few minutes ago.</summary>
    public bool Refresh { get; init; }
}
