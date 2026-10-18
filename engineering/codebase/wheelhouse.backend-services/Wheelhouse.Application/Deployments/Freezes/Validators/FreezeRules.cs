namespace Wheelhouse.Application.Deployments.Freezes.Validators;

/// <summary>Holds the rules freezes and deploy reasons share.</summary>
public static class FreezeRules
{
    /// <summary>Holds the longest reason the owner may give.</summary>
    public const int ReasonLength = 300;

    /// <summary>Whether a value names an environment a freeze can hold: null, <c>dev</c> or <c>prod</c>.</summary>
    /// <param name="environment">The value.</param>
    /// <returns><c>true</c> when it is acceptable.</returns>
    public static bool IsEnvironment(string? environment) => environment is null or "dev" or "prod";

    /// <summary>Whether a reason is acceptable: absent, or one line of at most <see cref="ReasonLength"/> characters.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns><c>true</c> when it is acceptable.</returns>
    public static bool IsReason(string? reason) =>
        reason is null || (reason.Length <= ReasonLength && !reason.Any(char.IsControl));
}
