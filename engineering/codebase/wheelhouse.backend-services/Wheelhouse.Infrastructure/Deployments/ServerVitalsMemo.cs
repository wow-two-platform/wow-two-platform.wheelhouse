using System.Text.Json;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Holds the last vitals snapshot for every request scope, so every tab and integration shares one runner read
/// per holding time; a single gate keeps concurrent misses to one read.</summary>
public sealed class ServerVitalsMemo
{
    /// <summary>Gets the gate one vitals read holds.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Gets or sets the last successful read and when it was taken.</summary>
    public (DateTimeOffset At, JsonElement Snapshot)? Last { get; set; }
}
