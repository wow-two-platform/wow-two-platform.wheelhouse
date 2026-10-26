namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents what the owner keeps stopped on an environment: the whole of it, or the services named. Every
/// deploy to it is refused until a start.</summary>
/// <param name="Environment">Whether the whole environment is stopped.</param>
/// <param name="Services">The services stopped one by one; empty when the whole environment is.</param>
/// <param name="At">When it was last stopped.</param>
/// <param name="Actor">Who stopped it.</param>
public sealed record TargetStopDto(bool Environment, IReadOnlyList<string> Services, DateTimeOffset? At, string? Actor);
