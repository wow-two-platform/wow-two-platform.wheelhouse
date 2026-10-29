using System.ComponentModel;
using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Holds one runner read of the deployment history: its records, or the failure that read returned. Immutable, so
/// the SDK cache hands the same instance to every request instead of a copy of the whole history.</summary>
/// <param name="Records">The records; empty with a failure.</param>
/// <param name="Failure">The read's failure, when it failed; a failed read is never held.</param>
[ImmutableObject(true)]
public sealed record DeploymentRecordsRead(IReadOnlyList<DeploymentRecord> Records, WoW.Two.Sdk.Beta.Be.Foundation.Errors.AppError? Failure);
