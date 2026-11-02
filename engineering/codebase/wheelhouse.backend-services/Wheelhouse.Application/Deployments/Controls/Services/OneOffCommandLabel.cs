using System.Text.RegularExpressions;

namespace Wheelhouse.Application.Deployments.Controls.Services;

/// <summary>Names a one-off command (v0.13) as the audit trail keeps it, by the runner's own rule, never with a value:
/// its leading words that read as a program, a file or a verb, then each option by name with <c>…</c> for its value —
/// <c>dotnet Identity.Api.dll admin create --slug … --phone …</c>; <c>--name=value</c> reads <c>--name=…</c>.</summary>
public static partial class OneOffCommandLabel
{
    /// <summary>Holds the longest label kept.</summary>
    public const int MaxLength = 200;

    /// <summary>Gets the label of <paramref name="command"/>.</summary>
    /// <param name="command">The command as the owner typed it, one argument per item.</param>
    /// <returns>The label; empty for no command.</returns>
    public static string Of(IReadOnlyList<string>? command)
    {
        List<string> words = [];
        var leading = true;
        foreach (var argument in command ?? [])
        {
            if (leading && Word().IsMatch(argument))
            {
                words.Add(argument);
                continue;
            }

            leading = false;
            var assigned = argument.IndexOf('=', StringComparison.Ordinal);
            var name = assigned < 0 ? argument : argument[..assigned];
            if (Option().IsMatch(name))
                words.Add(assigned < 0 ? name : name + "=…");
            else if (words.Count == 0 || words[^1] != "…")
                words.Add("…");
        }

        var label = string.Join(' ', words);
        return label.Length <= MaxLength ? label : label[..MaxLength];
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._/-]{0,63}$")]
    private static partial Regex Word();

    [GeneratedRegex("^--?[A-Za-z][A-Za-z0-9-]{0,63}$")]
    private static partial Regex Option();
}
