using System.Text.Json;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Reads the runner's answer to <c>generate-setting</c>: a clean exit prints <c>{"value": "…"}</c>, a failed one
/// <c>{"error": "…"}</c> (or the runner's own failure line, <c>{"reason": "…", "failure": "…"}</c>). The value is taken from a
/// clean exit's <c>value</c> alone; a failure keeps only its error line, cleaned and bounded, and never any other output.</summary>
public static class GeneratedSettingReply
{
    /// <summary>The longest error line kept, in characters.</summary>
    public const int MaxReasonLength = 300;

    /// <summary>Reads one run.</summary>
    /// <param name="exitCode">The runner's exit code.</param>
    /// <param name="output">Its standard output.</param>
    /// <param name="error">Its standard error.</param>
    /// <returns>The value, or the runner's error line: a refused precondition as a conflict, anything else as an
    /// unavailable step.</returns>
    public static AppResult<GeneratedSettingValue> Read(int exitCode, string? output, string? error)
    {
        if (exitCode == 0)
            return Json(output) is { } reply && reply.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                   && value.GetString() is { Length: > 0 } text
                ? AppResult<GeneratedSettingValue>.Ok(new GeneratedSettingValue(text))
                : AppResult<GeneratedSettingValue>.Fail(AppErrorFactory.ExternalUnavailable("the runner answered no value."));
        foreach (var stream in new[] { error, output })
        {
            if (Json(stream) is not { } line || (Text(line, "error") ?? Text(line, "reason")) is not { } reason
                || Clean(reason) is not { Length: > 0 } shown)
                continue;
            return AppResult<GeneratedSettingValue>.Fail(Text(line, "failure") == "Rejected"
                ? AppErrorFactory.Conflict(shown)
                : AppErrorFactory.ExternalUnavailable(shown));
        }
        return AppResult<GeneratedSettingValue>.Fail(AppErrorFactory.ExternalUnavailable(
            "the runner failed without an error line (exit " + exitCode + ")."));
    }

    /// <summary>The stream's JSON object: the whole text, else its last non-empty line; null for anything else.</summary>
    private static JsonElement? Json(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        var last = trimmed[(trimmed.LastIndexOf('\n') + 1)..].Trim();
        return Parse(trimmed) ?? (last.Length < trimmed.Length ? Parse(last) : null);
    }

    private static JsonElement? Parse(string text)
    {
        if (!text.StartsWith('{'))
            return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The error line without control characters, bounded. @internal</summary>
    private static string Clean(string reason)
    {
        var text = new string([.. reason.Where(character => !char.IsControl(character))]).Trim();
        return text.Length > MaxReasonLength ? text[..MaxReasonLength] : text;
    }
}
