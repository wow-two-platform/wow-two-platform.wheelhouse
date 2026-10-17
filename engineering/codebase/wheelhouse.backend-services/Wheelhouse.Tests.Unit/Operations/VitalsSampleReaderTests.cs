using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Operations;

namespace Wheelhouse.Tests.Unit.Operations;

/// <summary>Tests for <see cref="VitalsSampleReader"/>: one runner vitals pass into compact per-server host samples and
/// per-target container samples.</summary>
public sealed class VitalsSampleReaderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T12:00:00Z");

    private static JsonElement Vitals(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_readable_server_yields_its_own_load_memory_and_fullest_disk()
    {
        var samples = VitalsSampleReader.Read(Vitals("""
            {"collectedAt":"2026-09-28T12:00:00Z","servers":[{"serverId":"local","ok":true,"problems":[],
              "host":{"cpus":4,"load":[2.0,1.0,0.5],"memoryTotalBytes":1000,"memoryAvailableBytes":250,"uptimeSeconds":9,
                      "disks":[{"path":"/","totalBytes":100,"freeBytes":60},{"path":"/var/lib/docker","totalBytes":200,"freeBytes":20}]}}],
             "targets":[]}
            """), Now);

        var sample = samples.Should().ContainSingle().Subject;
        (sample.TargetId, sample.ServerId, sample.SampledAtUtc, sample.Readable).Should().Be(((string?)null, "local", Now, true));
        (sample.LoadPercent, sample.MemoryPercent, sample.DiskPercent).Should().Be((50.0, 75.0, 90.0));
        (sample.Containers, sample.HealthyContainers, sample.Restarts).Should().Be((0, 0, 0));
    }

    [Fact]
    public void A_readable_target_yields_container_health_and_leaves_the_host_to_its_server()
    {
        var samples = VitalsSampleReader.Read(Vitals("""
            {"collectedAt":"2026-09-28T12:00:00Z","targets":[{"targetId":"pin-dev","serverId":"local","ok":true,
              "containers":[{"service":"api","state":"running","health":"healthy","restarts":2},
                            {"service":"worker","state":"running","health":null,"restarts":0},
                            {"service":"edge","state":"exited","health":null,"restarts":5}]}]}
            """), Now);

        var sample = samples.Should().ContainSingle().Subject;
        (sample.TargetId, sample.ServerId, sample.SampledAtUtc, sample.Readable).Should().Be(("pin-dev", "local", Now, true));
        (sample.LoadPercent, sample.MemoryPercent, sample.DiskPercent).Should().Be(((double?)null, (double?)null, (double?)null));
        (sample.Containers, sample.HealthyContainers, sample.Restarts).Should().Be((3, 2, 7));
    }

    [Fact]
    public void Unreadable_servers_and_targets_are_kept_without_figures_and_malformed_rows_are_skipped()
    {
        var samples = VitalsSampleReader.Read(Vitals("""
            {"servers":[{"serverId":"fresh","ok":false,"reason":"Missing SSH identity file"},
                        {"ok":true},
                        "not-a-server",
                        {"serverId":"local","ok":true,"host":{"cpus":0,"load":[1,1,1]}}],
             "targets":[{"targetId":"pin-test","serverId":"local","ok":false,"reason":"SSH connection refused"},
                        {"serverId":"local","ok":true},
                        {"targetId":"pin-prod","serverId":"local","ok":true}]}
            """), Now);

        samples.Select(s => (s.TargetId, s.ServerId, s.Readable, s.LoadPercent, s.Containers)).Should().Equal(
            ((string?)null, "fresh", false, (double?)null, 0), (null, "local", true, null, 0),
            ("pin-test", "local", false, null, 0), ("pin-prod", "local", true, null, 0));
    }

    [Fact]
    public void A_pass_without_servers_or_targets_yields_nothing() =>
        VitalsSampleReader.Read(Vitals("""{"collectedAt":"2026-09-28T12:00:00Z"}"""), Now).Should().BeEmpty();
}
