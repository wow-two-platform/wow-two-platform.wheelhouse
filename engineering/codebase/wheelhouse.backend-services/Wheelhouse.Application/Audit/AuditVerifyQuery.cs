using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Audit;

/// <summary>Recomputes the audit chain and reports whether it is intact.</summary>
public sealed record AuditVerifyQuery : IQuery<AppResult<AuditVerification>>;
