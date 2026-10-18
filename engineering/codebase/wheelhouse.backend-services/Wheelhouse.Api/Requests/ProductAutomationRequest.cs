namespace Wheelhouse.Api.Requests;

/// <summary>Changes what the control plane deploys of a product on its own; an omitted choice stays as it is.</summary>
/// <param name="AutoDeployDev">Whether each new published release deploys to the product's dev targets.</param>
/// <param name="AutoPromote">Whether a release that passed its dev watch window is promoted to prod.</param>
public sealed record ProductAutomationRequest(bool? AutoDeployDev = null, bool? AutoPromote = null);
