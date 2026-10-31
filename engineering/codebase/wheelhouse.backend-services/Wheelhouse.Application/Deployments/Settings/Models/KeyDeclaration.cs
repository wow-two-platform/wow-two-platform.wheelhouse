using Wheelhouse.Domain.Settings.Enums;

namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents what a release or the owner declared about a key: its type and whether it is a secret.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Type">What its value must look like.</param>
/// <param name="Format">A date key's format.</param>
/// <param name="Choices">A choice key's values.</param>
/// <param name="Secret">Whether it is a secret.</param>
/// <param name="Plain">Whether a declaration names it a plain setting outright (typed, or optional with a default), so
/// its name does not read it as a secret.</param>
/// <param name="AdHoc">Whether the owner added it in the console.</param>
public sealed record KeyDeclaration(
    string Key, SettingType Type, string? Format, IReadOnlyList<string>? Choices, bool Secret, bool Plain, bool AdHoc)
{
    /// <summary>Gets how Wheelhouse makes the key's value when none is stored (<c>generate</c>); null when it does not.</summary>
    public SettingGenerateDto? Generate { get; init; }

    /// <summary>Gets the configuration path the key takes its value from (<c>from</c>); null for a key of its own. A linked
    /// key is never given a value.</summary>
    public string? From { get; init; }

    /// <summary>Gets the first service the release declares the key on; null for a key the owner added. A command that
    /// generates its value runs from this service's release image.</summary>
    public string? Service { get; init; }
}
