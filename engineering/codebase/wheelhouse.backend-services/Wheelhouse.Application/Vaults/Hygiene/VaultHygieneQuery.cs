using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Vaults.Hygiene;

/// <summary>Summarizes which of a vault's secrets and product tokens are due for rotation.</summary>
public sealed record VaultHygieneQuery(string Vault) : IQuery<AppResult<VaultHygiene>>;
