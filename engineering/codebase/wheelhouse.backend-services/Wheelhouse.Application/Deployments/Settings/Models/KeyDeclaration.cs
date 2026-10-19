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
    string Key, SettingType Type, string? Format, IReadOnlyList<string>? Choices, bool Secret, bool Plain, bool AdHoc);
