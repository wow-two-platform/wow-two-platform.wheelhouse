using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Audit;

/// <summary>Handles <see cref="AuditListQuery"/>.</summary>
public sealed class AuditListQueryHandler(IAuditTrail trail)
    : IQueryHandler<AuditListQuery, AppResult<IReadOnlyList<AuditEntryDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<AuditEntryDto>>> HandleAsync(
        AuditListQuery request, CancellationToken cancellationToken)
    {
        var entries = await trail.ListAsync(request.Limit, request.Before, cancellationToken);
        return AppResult<IReadOnlyList<AuditEntryDto>>.Ok([.. entries.Select(AuditEntryDto.From)]);
    }
}
