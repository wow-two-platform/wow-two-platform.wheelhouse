using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the record of what each deploy rendered, by fingerprint and without values.</summary>
public interface IDeploymentRenders
{
    /// <summary>Records one deploy's render.</summary>
    Task AddAsync(DeploymentRenderEntity render, CancellationToken ct = default);

    /// <summary>Gets one deploy's render, or null when it rendered nothing Wheelhouse recorded.</summary>
    Task<DeploymentRenderEntity?> GetAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Gets a target's latest render, or null.</summary>
    Task<DeploymentRenderEntity?> GetLatestAsync(string target, CancellationToken ct = default);

    /// <summary>Gets the keys each recorded deploy changed, by job; deploys that changed none are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> GetChangesAsync(CancellationToken ct = default);
}
