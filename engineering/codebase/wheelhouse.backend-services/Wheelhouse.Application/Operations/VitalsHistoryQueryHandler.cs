using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Operations;

/// <summary>Handles <see cref="VitalsHistoryQuery"/>.</summary>
public sealed class VitalsHistoryQueryHandler(IVitalsHistory history, TimeProvider time)
    : IQueryHandler<VitalsHistoryQuery, AppResult<IReadOnlyList<VitalsSampleDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<VitalsSampleDto>>> HandleAsync(
        VitalsHistoryQuery request, CancellationToken cancellationToken)
    {
        var samples = await history.ListAsync(request.Target, time.GetUtcNow().AddHours(-request.Hours), cancellationToken);
        return AppResult<IReadOnlyList<VitalsSampleDto>>.Ok([.. samples.Select(VitalsSampleDto.From)]);
    }
}
