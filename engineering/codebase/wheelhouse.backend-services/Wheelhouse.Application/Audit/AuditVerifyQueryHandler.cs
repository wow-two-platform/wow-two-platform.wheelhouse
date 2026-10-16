using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Audit;

/// <summary>Handles <see cref="AuditVerifyQuery"/>.</summary>
public sealed class AuditVerifyQueryHandler(IAuditTrail trail) : IQueryHandler<AuditVerifyQuery, AppResult<AuditVerification>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<AuditVerification>> HandleAsync(AuditVerifyQuery request, CancellationToken cancellationToken) =>
        AppResult<AuditVerification>.Ok(await trail.VerifyAsync(cancellationToken));
}
