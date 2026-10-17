namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a site a rollout published and whether it answered through the ingress right after.</summary>
/// <param name="Name">The site's name in the product's deploy descriptor.</param>
/// <param name="Service">The service the site routes to.</param>
/// <param name="Exposure"><c>public</c> or <c>private</c>.</param>
/// <param name="Url">The site's address.</param>
/// <param name="Probe">The probe's outcome; null when the ingress has no probe address.</param>
public sealed record DeploymentSiteDto(string Name, string? Service, string? Exposure, string Url, DeploymentSiteProbeDto? Probe);
