using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Queries;

/// <summary>Reads a secret's versions in a target's environment: who, when, fingerprint and note; never a value.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Key">The secret's key.</param>
public sealed record SecretHistoryQuery(string Target, string Key) : IQuery<AppResult<SecretHistoryDto>>;
