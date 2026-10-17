namespace Wheelhouse.Application.Pins.Models;

/// <summary>Represents the answer to a pin request: the pin, and whether this request made it or it already existed.</summary>
/// <param name="Pin">The pin on the requested target.</param>
/// <param name="Created">Whether the request made the pin.</param>
public sealed record PinCreated(PinDto Pin, bool Created);
