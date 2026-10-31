namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents how Wheelhouse makes a secret key's value, as the release declares it (<c>generate</c>): random
/// bytes in an encoding, or a command run from the declaring service's release image. A declaration, never a value.</summary>
/// <param name="Random">How many random bytes, 16-64; null for the command form.</param>
/// <param name="Encoding"><c>base64url</c> (the default) or <c>hex</c>; null for the command form.</param>
/// <param name="Command">The command's argument list; null for the random form.</param>
/// <param name="TimeoutSeconds">How long the command may run, in seconds; null for the default (two minutes).</param>
public sealed record SettingGenerateDto(
    int? Random = null, string? Encoding = null, IReadOnlyList<string>? Command = null, int? TimeoutSeconds = null)
{
    /// <summary>How long a command runs when its declaration names no limit: two minutes.</summary>
    public const int DefaultTimeoutSeconds = 120;

    /// <summary>The longest a command may run: ten minutes.</summary>
    public const int MaxTimeoutSeconds = 600;

    /// <summary>Gets whether the value comes from a command rather than random bytes.</summary>
    public bool IsCommand => Command is { Count: > 0 };

    /// <summary>Gets how the audit and the version's origin name the form, such as <c>random 32 bytes base64url</c> or
    /// <c>command</c>; never the command's output.</summary>
    public string Kind => IsCommand ? "command" : "random " + Random + " bytes " + (Encoding ?? Services.SecretValues.Base64Url);

    /// <summary>Gets how long the command may run, its declaration bounded to 1 second to <see cref="MaxTimeoutSeconds"/>.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds ?? DefaultTimeoutSeconds, 1, MaxTimeoutSeconds));
}
