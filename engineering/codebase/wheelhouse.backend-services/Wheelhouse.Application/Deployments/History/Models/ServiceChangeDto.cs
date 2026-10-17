namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one service of a rollout against what its target ran before it.</summary>
/// <param name="Service">The service's key.</param>
/// <param name="From">The version (or short image digest) the target ran before; null when it ran none.</param>
/// <param name="To">The version (or short image digest) the rollout deployed; null when the release dropped it.</param>
/// <param name="Changed">Whether the image differs from the one the target ran before.</param>
public sealed record ServiceChangeDto(string Service, string? From, string? To, bool Changed);
