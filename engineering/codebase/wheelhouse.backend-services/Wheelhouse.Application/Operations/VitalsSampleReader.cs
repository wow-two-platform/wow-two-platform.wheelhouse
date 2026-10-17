using System.Text.Json;
using Wheelhouse.Domain.Operations.Entities;

namespace Wheelhouse.Application.Operations;

/// <summary>Turns one runner vitals pass into compact samples: one per server with its host figures, one per target
/// with its container health. It reads only figures: never labels, environment or logs, which the runner never returns
/// anyway.</summary>
public static class VitalsSampleReader
{
    /// <summary>Reads every server and target of a vitals pass; one the runner could not read yields an unreadable
    /// sample.</summary>
    public static IReadOnlyList<VitalsSample> Read(JsonElement vitals, DateTimeOffset sampledAt)
    {
        var samples = new List<VitalsSample>();
        // A server reads its own host, so one that runs no target yet still has a trend.
        foreach (var server in Items(vitals, "servers"))
        {
            var serverId = Text(server, "serverId");
            if (serverId is null)
                continue;
            var sample = new VitalsSample
            {
                Id = Guid.NewGuid(), TargetId = null, ServerId = serverId, SampledAtUtc = sampledAt, Readable = Ok(server)
            };
            if (server.TryGetProperty("host", out var host) && host.ValueKind == JsonValueKind.Object)
                ReadHost(host, sample);
            samples.Add(sample);
        }
        foreach (var target in Items(vitals, "targets"))
        {
            var targetId = Text(target, "targetId");
            var serverId = Text(target, "serverId");
            if (targetId is null || serverId is null)
                continue;
            var sample = new VitalsSample
            {
                Id = Guid.NewGuid(), TargetId = targetId, ServerId = serverId, SampledAtUtc = sampledAt, Readable = Ok(target)
            };
            if (target.TryGetProperty("containers", out var containers) && containers.ValueKind == JsonValueKind.Array)
                foreach (var container in containers.EnumerateArray())
                {
                    sample.Containers++;
                    var state = Text(container, "state");
                    var health = Text(container, "health");
                    if (state == "running" && health is null or "healthy")
                        sample.HealthyContainers++;
                    sample.Restarts += (int)(Number(container, "restarts") ?? 0);
                }
            samples.Add(sample);
        }
        return samples;
    }

    /// <summary>Reads a host's one-minute load against its CPUs, its memory in use and its fullest disk.</summary>
    private static void ReadHost(JsonElement host, VitalsSample sample)
    {
        var cpus = Number(host, "cpus");
        if (cpus is > 0 && host.TryGetProperty("load", out var load) && load.ValueKind == JsonValueKind.Array
            && load.GetArrayLength() > 0 && load[0].TryGetDouble(out var oneMinute))
            sample.LoadPercent = Round(oneMinute / cpus.Value * 100);
        var total = Number(host, "memoryTotalBytes");
        var available = Number(host, "memoryAvailableBytes");
        if (total is > 0 && available is not null)
            sample.MemoryPercent = Round((total.Value - available.Value) / total.Value * 100);
        if (host.TryGetProperty("disks", out var disks) && disks.ValueKind == JsonValueKind.Array)
            foreach (var disk in disks.EnumerateArray())
            {
                var capacity = Number(disk, "totalBytes");
                var free = Number(disk, "freeBytes");
                if (capacity is > 0 && free is not null)
                    sample.DiskPercent = Math.Max(sample.DiskPercent ?? 0, Round((capacity.Value - free.Value) / capacity.Value * 100));
            }
    }

    private static IEnumerable<JsonElement> Items(JsonElement vitals, string name) =>
        vitals.ValueKind == JsonValueKind.Object && vitals.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static bool Ok(JsonElement element) =>
        element.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static double Round(double value) => Math.Round(value, 1);
}
