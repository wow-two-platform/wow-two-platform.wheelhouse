using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Services;

/// <summary>Names the failures of a Files host call: an <c>ExternalUnavailable</c> error whose message starts with
/// <see cref="Prefix"/> and carries the status only, so the API answers 502 and no body or key ever shows.</summary>
public static class FilesImportErrors
{
    /// <summary>Holds the message prefix that marks a Files host failure.</summary>
    public const string Prefix = "Files host ";

    /// <summary>Creates the failure for a Files status.</summary>
    /// <param name="status">The HTTP status Files answered.</param>
    /// <returns>The error.</returns>
    public static AppError Answered(int status) => AppErrorFactory.ExternalUnavailable($"{Prefix}answered {status}.");

    /// <summary>Creates the failure for a Files host that did not answer.</summary>
    /// <returns>The error.</returns>
    public static AppError Unreachable() => AppErrorFactory.ExternalUnavailable($"{Prefix}could not be reached.");

    /// <summary>Whether <paramref name="error"/> is a Files host failure.</summary>
    /// <param name="error">The error.</param>
    /// <returns><c>true</c> for an error made here.</returns>
    public static bool Is(AppError error) =>
        error.Type == AppErrorType.ExternalUnavailable && error.Message.StartsWith(Prefix, StringComparison.Ordinal);
}
