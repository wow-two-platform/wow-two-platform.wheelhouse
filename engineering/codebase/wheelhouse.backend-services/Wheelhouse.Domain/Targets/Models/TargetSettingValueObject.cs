namespace Wheelhouse.Domain.Targets.Models;

/// <summary>Represents where one service's settings file lives on its target's host.</summary>
public sealed record TargetSettingValueObject
{
    /// <summary>Gets the service's name in the product's Compose definition.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the settings file's absolute path on the host.</summary>
    public required string Path { get; init; }
}
