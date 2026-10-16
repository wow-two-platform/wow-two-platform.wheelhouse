using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to record where a catalog product stands in the portfolio.</summary>
public sealed record ProductLifecycleUpdateCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand
{
    /// <summary>Gets the product's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets where the product now stands.</summary>
    public required ProductLifecycle Lifecycle { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.lifecycle";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => Lifecycle.ToString();
}
