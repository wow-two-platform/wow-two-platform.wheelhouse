using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the interrupted or failed rollout the owner inspected; accepts nothing else.</summary>
public sealed record DeploymentReconcileRequest([Required] Guid? Job);
