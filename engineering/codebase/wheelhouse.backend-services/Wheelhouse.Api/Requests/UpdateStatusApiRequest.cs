using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to mark a product or a service active or inactive.</summary>
public sealed record UpdateStatusApiRequest
{
    /// <summary>Gets the new status: <c>active</c> or <c>inactive</c>.</summary>
    public required ProductStatus Status { get; init; }
}
