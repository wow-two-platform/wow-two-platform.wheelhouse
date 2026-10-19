using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Gives a target's environment setting values: secrets go to its vault as new versions, the rest to
/// Wheelhouse's settings store, all in one transaction. One value is one field's save; several are a bulk edit that
/// lands whole or not at all. The audit trail names the keys and versions, never a value.</summary>
/// <param name="Target">The target's slug; the values belong to its product environment.</param>
/// <param name="Values">The values.</param>
/// <param name="Actor">Who gave them.</param>
/// <param name="Release">The release whose declarations type the values; null for the one the target runs.</param>
public sealed record DeploymentSettingsSaveCommand(
    string Target, IReadOnlyList<DeploySettingValue> Values, string Actor, string? Release = null)
    : ICommand<AppResult<DeploySettingsSavedDto>>, IAuditedCommand
{
    /// <summary>Gets what the save wrote, filled by its handler: each key with its new version, for the audit detail.</summary>
    public SettingsSaveRecord Saved { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "settings.save";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Saved.Keys.Count > 0
        ? string.Join(", ", Saved.Keys.Select(Describe))
        : Describe(false, "setting") + Describe(true, "secret");

    /// <summary>Returns the command without its values, so logs and exceptions cannot print one.</summary>
    public override string ToString() => nameof(DeploymentSettingsSaveCommand) + " { " + Target + ": " + AuditDetail + " }";

    private static string Describe(SavedSettingDto key) =>
        key.Key + (key.Removed ? " removed" : key.Secret ? " secret" + (key.Version is { } version ? " v" + version : "") : " setting");

    private string Describe(bool secret, string noun)
    {
        var keys = Values.Where(value => value.Secret == secret).Select(value => value.Key).Order(StringComparer.Ordinal).ToList();
        return keys.Count == 0 ? "" : (secret && Values.Any(value => !value.Secret) ? "; " : "")
            + keys.Count + " " + noun + (keys.Count == 1 ? "" : "s") + " (" + string.Join(", ", keys) + ")";
    }
}
