using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Inventory;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Catalog.Commands;

/// <summary>Represents a command to import catalog entries: their workspaces are created when missing, entries
/// Wheelhouse deploys become products, and existing products move into their entry's workspace. Running it again
/// changes nothing more.</summary>
public sealed record CatalogImportCommand : ICommand<AppResult<CatalogImportResultDto>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slugs of the entries to import.</summary>
    public required IReadOnlyList<string> Slugs { get; init; }

    /// <inheritdoc />
    public string AuditAction => "catalog.import";

    /// <inheritdoc />
    public string AuditSubject => "catalog";

    /// <inheritdoc />
    public string? AuditDetail
    {
        get
        {
            var detail = string.Join(", ", Slugs ?? []);
            return detail.Length <= 300 ? detail : detail[..299] + "…";
        }
    }
}
