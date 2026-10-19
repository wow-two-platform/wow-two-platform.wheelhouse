using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Queries;

/// <summary>Reads what the built-in vault needs the owner for: a sealed vault, and secrets lapsing within 30 days or
/// lapsed.</summary>
public sealed record VaultIssuesQuery : IQuery<AppResult<VaultIssuesDto>>;
