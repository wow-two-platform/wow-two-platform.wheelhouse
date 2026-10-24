namespace Wheelhouse.Application.Targets.Models;

/// <summary>Represents what Wheelhouse derives for its own environments (decision 64): nothing about them is typed.</summary>
public sealed record SelfTargetDto
{
    /// <summary>Gets Wheelhouse's own product slug.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the hostname this console answers on, which both consoles' site takes; <c>null</c> when its
    /// settings name none.</summary>
    public string? Host { get; init; }

    /// <summary>Gets each environment's derived definition, in promotion order.</summary>
    public required IReadOnlyList<SelfTargetEnvironmentDto> Environments { get; init; }
}
