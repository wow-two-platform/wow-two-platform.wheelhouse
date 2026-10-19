using System.Globalization;
using System.Text.RegularExpressions;
using Wheelhouse.Domain.Settings.Enums;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>What each setting type accepts, and which key names read as secrets. The release generator
/// (<c>wow-two-platform.pipelines</c>, <c>generator/release.py</c>) checks declared defaults by the same rules.</summary>
public static partial class SettingTypes
{
    /// <summary>The longest list a choice key may declare.</summary>
    public const int MaxChoices = 50;

    /// <summary>The type names, as descriptors, bundles and the API write them.</summary>
    public static readonly IReadOnlyList<string> Names = ["text", "number", "boolean", "choice", "url", "cron", "date", "email"];

    /// <summary>Reads a type name; null for an unknown one.</summary>
    /// <param name="name">The name, such as <c>cron</c>.</param>
    /// <returns>The type.</returns>
    public static SettingType? Parse(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "text" => SettingType.Text,
        "number" => SettingType.Number,
        "boolean" => SettingType.Boolean,
        "choice" => SettingType.Choice,
        "url" or "link" => SettingType.Url,
        "cron" => SettingType.Cron,
        "date" => SettingType.Date,
        "email" => SettingType.Email,
        _ => null,
    };

    /// <summary>The type's name, as the wire carries it.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Its name.</returns>
    public static string NameOf(SettingType type) => type.ToString().ToLowerInvariant();

    /// <summary>Whether a date format uses only <c>yyyy</c>, <c>MM</c>, <c>dd</c>, <c>HH</c>, <c>mm</c>, <c>ss</c> and the
    /// separators <c>- . / : T</c> and space, with a year, month and day.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>true</c> when it is one.</returns>
    public static bool IsDateFormat(string? format) =>
        format is { Length: <= 40 } && DateFormat().IsMatch(format)
        && format.Contains("yyyy", StringComparison.Ordinal) && format.Contains("MM", StringComparison.Ordinal)
        && format.Contains("dd", StringComparison.Ordinal);

    /// <summary>The date format a date key takes when its declaration names none.</summary>
    public const string DefaultDateFormat = "yyyy-MM-dd";

    /// <summary>Checks a value against a type; returns why it does not fit, or null.</summary>
    /// <param name="type">The type.</param>
    /// <param name="value">The value.</param>
    /// <param name="format">A date key's format.</param>
    /// <param name="choices">A choice key's values.</param>
    /// <returns>The display-safe problem, which never quotes the value, or null when it fits.</returns>
    public static string? Check(SettingType type, string value, string? format = null, IReadOnlyCollection<string>? choices = null) =>
        type switch
        {
            SettingType.Number => NumberValue().IsMatch(value) ? null : "must be a number, such as 42 or 0.5",
            SettingType.Boolean => value is "true" or "false" ? null : "must be true or false",
            SettingType.Choice => choices is { Count: > 0 } && choices.Contains(value, StringComparer.Ordinal)
                ? null
                : "must be one of " + string.Join(", ", (choices ?? []).Take(10)),
            SettingType.Url => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                                                                         && !string.IsNullOrEmpty(uri.Host) && !value.Any(char.IsWhiteSpace)
                ? null
                : "must be an absolute http or https link",
            SettingType.Cron => IsCron(value) ? null : "must be a cron expression of five or six fields, or @daily and the like",
            SettingType.Date => DateTime.TryParseExact(value, format ?? DefaultDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)
                ? null
                : "must be a date as " + (format ?? DefaultDateFormat),
            SettingType.Email => EmailValue().IsMatch(value) ? null : "must be an email address",
            _ => null,
        };

    /// <summary>Whether a key's name reads as a secret: its last section ends in password, passphrase, secret, token,
    /// key, pwd, credential(s), connectionstring or salt, or it sits under <c>ConnectionStrings</c>. The runner guesses
    /// the same way.</summary>
    /// <param name="key">The configuration path.</param>
    /// <returns><c>true</c> when it reads as one.</returns>
    public static bool LooksSecret(string key)
    {
        var sections = key.Split(':');
        return SecretName().IsMatch(sections[^1])
               || (sections.Length > 1 && string.Equals(sections[0], "ConnectionStrings", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a value is a cron expression: five fields (minute, hour, day, month, weekday), six with seconds
    /// first, or a macro; each field <c>*</c>, <c>?</c>, numbers, names, ranges, steps and lists within its range.</summary>
    /// <param name="value">The expression.</param>
    /// <returns><c>true</c> when it is one.</returns>
    public static bool IsCron(string value)
    {
        var trimmed = value.Trim();
        if (trimmed != value || trimmed.Length is 0 or > 200)
            return false;
        if (trimmed.StartsWith('@'))
            return trimmed is "@yearly" or "@annually" or "@monthly" or "@weekly" or "@daily" or "@midnight" or "@hourly";
        var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        (int Min, int Max)[] ranges = fields.Length switch
        {
            5 => [(0, 59), (0, 23), (1, 31), (1, 12), (0, 7)],
            6 => [(0, 59), (0, 59), (0, 23), (1, 31), (1, 12), (0, 7)],
            _ => [],
        };
        if (ranges.Length == 0)
            return false;
        for (var index = 0; index < fields.Length; index++)
        {
            // Five fields start at the minute; a sixth, leading, is the second (position -1).
            var position = index - (fields.Length - 5);
            var names = position switch { 3 => Months, 4 => Days, _ => null };
            var lastDay = position is 2 or 4;
            if (!fields[index].Split(',').All(part => CronPart(part, ranges[index], names, lastDay)))
                return false;
        }
        return true;
    }

    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] Days = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private static bool CronPart(string part, (int Min, int Max) range, string[]? names, bool allowsLast)
    {
        if (part.Length == 0)
            return false;
        var step = part.Split('/');
        if (step.Length > 2 || (step.Length == 2 && !(int.TryParse(step[1], NumberStyles.None, CultureInfo.InvariantCulture, out var by) && by > 0)))
            return false;
        var head = step[0];
        if (head is "*" or "?")
            return head == "*" || step.Length == 1;
        if (allowsLast && step.Length == 1 && (head == "L" || head.EndsWith('L') || head.Contains('#') || head.EndsWith('W')))
            return CronToken(head.TrimEnd('L', 'W').Split('#')[0], range, names, emptyOk: head == "L")
                   && (!head.Contains('#') || int.TryParse(head.Split('#')[1], NumberStyles.None, CultureInfo.InvariantCulture, out var nth) && nth is >= 1 and <= 5);
        var bounds = head.Split('-');
        if (bounds.Length > 2 || !bounds.All(bound => CronToken(bound, range, names)))
            return false;
        return bounds.Length == 1 || Value(bounds[0], names) <= Value(bounds[1], names);
    }

    private static bool CronToken(string token, (int Min, int Max) range, string[]? names, bool emptyOk = false)
    {
        if (token.Length == 0)
            return emptyOk;
        var value = Value(token, names);
        return value >= range.Min && value <= range.Max;
    }

    private static int Value(string token, string[]? names)
    {
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return number;
        var index = names is null ? -1 : Array.IndexOf(names, token.ToUpperInvariant());
        return index < 0 ? -1 : names == Months ? index + 1 : index;
    }

    [GeneratedRegex(@"^(yyyy|MM|dd|HH|mm|ss|[-./: T])+$")]
    private static partial Regex DateFormat();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]{0,30})(\.[0-9]{1,30})?$")]
    private static partial Regex NumberValue();

    [GeneratedRegex(@"^[^@\s]{1,64}@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex EmailValue();

    [GeneratedRegex(@"(password|passphrase|secret|token|apikey|key|pwd|credentials?|connectionstring|salt)$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();
}
