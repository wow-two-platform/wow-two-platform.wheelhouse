using Wheelhouse.Application.Pins.Models;
using Wheelhouse.Domain.Pins.Entities;

namespace Wheelhouse.Application.Pins.Mappers;

/// <summary>Maps a stored pin to the one the console lists.</summary>
internal static class PinMapper
{
    /// <summary>Maps a stored pin to its projection.</summary>
    /// <param name="pin">The stored pin.</param>
    /// <returns>The pin the console lists.</returns>
    public static PinDto Map(PinEntity pin) => new()
    {
        Id = pin.Id,
        Kind = pin.Kind,
        WorkspaceId = pin.WorkspaceId,
        ProductId = pin.ProductId,
        Service = pin.Service,
        Order = pin.Order,
    };
}
