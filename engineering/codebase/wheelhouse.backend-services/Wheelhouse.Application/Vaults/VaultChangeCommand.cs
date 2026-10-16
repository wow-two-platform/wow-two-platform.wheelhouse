using System.Text.Json;
using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Vaults.Changes;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Vaults;

/// <summary>Applies one administrative change to a vault on behalf of an operator.</summary>
public sealed record VaultChangeCommand(string Vault, VaultChange Change, string Actor)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "vault." + Change switch
    {
        NamespaceCreateChange => "namespace.create",
        SecretSetChange => "secret.set",
        SecretStateChange => "secret.state",
        TokenMintChange => "token.mint",
        TokenRevokeChange => "token.revoke",
        _ => "change"
    };

    /// <inheritdoc />
    public string AuditSubject => Vault + "/" + Change.Namespace;

    /// <inheritdoc />
    public string AuditDetail => Change.Describe();
}
