using System.ComponentModel.DataAnnotations;
using Wheelhouse.Domain.Deployments.Enums;

namespace Wheelhouse.Api.Requests;

/// <summary>Stops, starts or restarts the release an environment runs: one service, or the whole environment without
/// one; or resets the environment's database.</summary>
/// <param name="Operation"><c>stop</c>, <c>start</c>, <c>restart</c> or <c>reset</c>; a restart names a service, a reset
/// none.</param>
/// <param name="Service">The service's key; null for the whole environment.</param>
/// <param name="Reason">The owner's one-line why, kept on the job; prod needs one.</param>
/// <param name="Confirm">The target ID, typed; prod needs it.</param>
public sealed record TargetControlRequest([Required] ControlOperation? Operation, string? Service = null, string? Reason = null,
    string? Confirm = null);
