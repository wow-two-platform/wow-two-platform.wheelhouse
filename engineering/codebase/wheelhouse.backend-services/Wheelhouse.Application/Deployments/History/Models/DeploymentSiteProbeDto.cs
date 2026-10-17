namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents whether a site answered through the ingress by its host name.</summary>
/// <param name="Ok">Whether it answered.</param>
/// <param name="Status">The HTTP status it answered with.</param>
/// <param name="Detail">Why it did not answer.</param>
public sealed record DeploymentSiteProbeDto(bool Ok, int? Status, string? Detail);
