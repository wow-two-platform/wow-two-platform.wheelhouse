namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents the last scheduled check of the root key file: present, readable by the console alone, and
/// opening the canary.</summary>
/// <param name="At">When the check ran.</param>
/// <param name="Problem">What failed: <c>missing</c>, <c>unreadable</c> (the console's user may not read it),
/// <c>exposed</c> (its mode lets others than its owner in), <c>invalid</c> or <c>mismatch</c>; null when it passed.</param>
public sealed record RootKeyFileCheckDto(DateTimeOffset At, string? Problem);
