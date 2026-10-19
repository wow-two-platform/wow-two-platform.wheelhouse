using System.Text.Json;
using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Places a product's dev and prod databases and its private sites on a server, in place of the runbook's
/// hand-run steps.</summary>
public sealed record ProductPlaceCommand : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <summary>Gets the product's slug.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the server's slug.</summary>
    public required string Server { get; init; }

    /// <summary>Gets whether to create the product's dev and prod databases.</summary>
    public bool Database { get; init; }

    /// <summary>Gets the private sites and their tailnet nodes.</summary>
    public IReadOnlyList<ProductPlacementSite> Sites { get; init; } = [];

    /// <summary>Gets who places it.</summary>
    public required string Actor { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.place";

    /// <inheritdoc />
    public string AuditSubject => Product;

    /// <inheritdoc />
    public string AuditDetail => "on " + Server + ": " + string.Join(", ",
        (Database ? ["databases"] : Array.Empty<string>()).Concat(Sites.Select(site => site.Site + "=" + site.Node)));
}
