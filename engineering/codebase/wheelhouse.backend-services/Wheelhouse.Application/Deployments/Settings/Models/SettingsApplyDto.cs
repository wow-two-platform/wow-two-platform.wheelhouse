namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents an apply of edited settings: submitted now as a deploy of the running release, or scheduled.</summary>
/// <param name="Target">The target.</param>
/// <param name="Mode"><c>now</c> or <c>scheduled</c>.</param>
/// <param name="At">When a scheduled apply runs.</param>
/// <param name="By">Who asked for it.</param>
/// <param name="Deployment">The deploy an apply now submitted.</param>
/// <param name="Release">The release it deploys again.</param>
public sealed record SettingsApplyDto(string Target, string Mode, DateTimeOffset? At, string By, string? Deployment, string? Release);
