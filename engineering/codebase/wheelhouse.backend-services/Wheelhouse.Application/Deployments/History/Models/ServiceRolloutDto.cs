namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents how a rollout treated each service: what the release changed against the release its target ran,
/// the dependency waves it started the changed services in, and how each one ended.</summary>
/// <param name="Staged">Whether the changed services started wave by wave rather than in one Compose <c>up</c>.</param>
/// <param name="Waves">The changed services in start order; a wave started after the waves it depends on were healthy.</param>
/// <param name="Removed">The services the release dropped.</param>
/// <param name="Services">Each service the release declares.</param>
public sealed record ServiceRolloutDto(
    bool Staged, IReadOnlyList<IReadOnlyList<string>> Waves, IReadOnlyList<string> Removed,
    IReadOnlyList<ServiceRolloutItemDto> Services);
