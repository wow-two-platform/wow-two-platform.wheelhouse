namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one page of deployments, newest first.</summary>
/// <param name="Items">The page's deployments.</param>
/// <param name="NextCursor">The cursor of the next page; null on the last one.</param>
public sealed record DeploymentPageDto(IReadOnlyList<DeploymentItemDto> Items, string? NextCursor);
