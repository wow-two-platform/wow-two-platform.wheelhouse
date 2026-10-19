using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Queries;

/// <summary>Reads the built-in vault's state: sealed or not and why, where the root key is read from, what it holds.</summary>
public sealed record VaultStatusQuery : IQuery<AppResult<VaultStatusDto>>;
