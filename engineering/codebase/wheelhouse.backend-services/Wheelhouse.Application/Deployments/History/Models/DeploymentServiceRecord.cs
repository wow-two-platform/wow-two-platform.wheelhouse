namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one service of a release bundle: its immutable image and its declared version.</summary>
/// <param name="Image">The image reference with its digest, the deployment identity.</param>
/// <param name="Version">The version the release declares for the service, when it declares one.</param>
public sealed record DeploymentServiceRecord(string? Image, string? Version);
