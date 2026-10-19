using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Gives a target's environment setting values: secrets go to its vault namespace, the rest to Wheelhouse's
/// settings store. The audit trail names the keys, never a value.</summary>
/// <param name="Target">The target's slug; the values belong to its product environment.</param>
/// <param name="Values">The values.</param>
/// <param name="Actor">Who gave them.</param>
public sealed record DeploymentSettingsSaveCommand(string Target, IReadOnlyList<DeploySettingValue> Values, string Actor)
    : ICommand<AppResult<DeploySettingsSavedDto>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "settings.save";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Describe(false, "setting") + Describe(true, "secret");

    /// <summary>Returns the command without its values, so logs and exceptions cannot print one.</summary>
    public override string ToString() => nameof(DeploymentSettingsSaveCommand) + " { " + Target + ": " + AuditDetail + " }";

    private string Describe(bool secret, string noun)
    {
        var keys = Values.Where(value => value.Secret == secret).Select(value => value.Key).Order(StringComparer.Ordinal).ToList();
        return keys.Count == 0 ? "" : (secret && Values.Any(value => !value.Secret) ? "; " : "")
            + keys.Count + " " + noun + (keys.Count == 1 ? "" : "s") + " (" + string.Join(", ", keys) + ")";
    }
}
