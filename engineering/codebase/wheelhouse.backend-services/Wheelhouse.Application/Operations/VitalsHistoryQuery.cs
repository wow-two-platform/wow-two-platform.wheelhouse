using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Operations;

/// <summary>Reads stored vitals samples of the last hours: one target's, or every server's and target's.</summary>
/// <param name="Target">One target, or every server and target when null.</param>
/// <param name="Hours">How far back, 1-720.</param>
public sealed record VitalsHistoryQuery(string? Target, int Hours) : IQuery<AppResult<IReadOnlyList<VitalsSampleDto>>>;
