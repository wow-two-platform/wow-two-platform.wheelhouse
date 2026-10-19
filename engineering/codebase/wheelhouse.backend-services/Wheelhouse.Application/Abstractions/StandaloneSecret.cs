namespace Wheelhouse.Application.Abstractions;

/// <summary>Represents one secret of a standalone vault.</summary>
/// <param name="Key">The secret's key.</param>
/// <param name="Disabled">Whether it was disabled there.</param>
/// <param name="CurrentVersion">The version it was on.</param>
/// <param name="Versions">Every version it kept.</param>
public sealed record StandaloneSecret(string Key, bool Disabled, int CurrentVersion, IReadOnlyList<StandaloneVersion> Versions);
