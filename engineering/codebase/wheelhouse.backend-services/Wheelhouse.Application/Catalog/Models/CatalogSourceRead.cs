using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents one hub's read: where it came from and, when its entries are not listed, why.</summary>
/// <param name="Source">The hub, the commit read and the error the console shows.</param>
/// <param name="Failure">The error the read gave, or <c>null</c> when the hub's entries are listed.</param>
public sealed record CatalogSourceRead(CatalogSourceDto Source, AppError? Failure);
