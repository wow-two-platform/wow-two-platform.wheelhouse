namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a service a deployment's origin likely hurt: it turned unhealthy after the origin did and declares
/// a path to it. Inferred from the declaration, never an observed call.</summary>
/// <param name="Service">The affected service's key.</param>
/// <param name="Finding">What was found wrong with it.</param>
/// <param name="Via">The origin service it declares it depends on, directly or through others.</param>
/// <param name="At">When it turned unhealthy, when known.</param>
public sealed record DeploymentEffectDto(string Service, string Finding, string Via, DateTimeOffset? At);
