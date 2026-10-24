using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Wheelhouse.Application.Servers.Services;
using Wheelhouse.Application.Targets.Services;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Infrastructure.Deployments;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>What Wheelhouse knows about itself for its own environments: the hostname this console answers on, read from
/// its own settings; which server is the control host; and that a console naming no hostname saves no environment of
/// its own.</summary>
public sealed class SelfTargetsTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    [Theory]
    // The kit's console-<env>.json, as CI and self-deploy.py mount it.
    [InlineData(null, "wheelhouse.tail1234.ts.net;localhost", "wheelhouse.tail1234.ts.net")]
    // A Wheelhouse render: the site's host, the container's name, a host the host file kept, localhost.
    [InlineData(null, "localhost;wheelhouse-dev-console;Wheelhouse.Tail1234.ts.net", "wheelhouse.tail1234.ts.net")]
    // The render's own site URL wins, whatever AllowedHosts lists first.
    [InlineData("https://wheelhouse.tail1234.ts.net:8443", "old.example;localhost", "wheelhouse.tail1234.ts.net")]
    [InlineData("not a url", "*.example;172.30.1.4;wheelhouse.tail1234.ts.net", "wheelhouse.tail1234.ts.net")]
    [InlineData(null, "*", null)]
    [InlineData(null, "localhost;172.30.1.4", null)]
    [InlineData(null, null, null)]
    public void Host_ShouldBeTheConsolesOwnSiteOrFirstNamedAllowedHost(string? siteUrl, string? allowedHosts, string? expected)
    {
        var values = new List<(string, string)>();
        if (siteUrl is not null)
            values.Add((ConsoleIdentity.SiteUrlKey, siteUrl));
        if (allowedHosts is not null)
            values.Add(("AllowedHosts", allowedHosts));

        ConsoleIdentity.HostOf(Settings([.. values])).Should().Be(expected);
    }

    [Theory]
    [InlineData(VpsProvider.Ovhcloud, "172.30.0.1", true)]
    [InlineData(VpsProvider.Hetzner, "10.0.0.2", true)]
    [InlineData(VpsProvider.Contabo, "192.168.1.10", true)]
    [InlineData(VpsProvider.Ovhcloud, "172.32.0.1", false)]
    [InlineData(VpsProvider.Ovhcloud, "100.88.0.1", false)]
    [InlineData(VpsProvider.Ovhcloud, "203.0.113.20", false)]
    [InlineData(VpsProvider.Ovhcloud, "vps.example.net", false)]
    [InlineData(VpsProvider.Local, "172.30.0.1", false)]
    public void ControlHost_ShouldBeTheServerAtAPrivateAddress_NeverTheLocalRig(VpsProvider provider, string host, bool expected) =>
        ControlHost.Is(provider, host).Should().Be(expected);

    [Fact]
    public void Refusal_ShouldSaveNothing_WhileTheConsoleNamesNoHostname()
    {
        var control = new ServerEntity
        {
            Id = Guid.NewGuid(), Slug = "control", Name = "Control", Provider = VpsProvider.Ovhcloud, Host = "172.30.0.1",
            Region = "control", SshUser = "wheelhouse-deploy", SshPort = 22, Ingress = new ServerIngressValueObject(),
        };
        var derived = SelfTargets.Derive(DeploymentEnvironment.Prod, null);

        derived.Sites.Should().BeEmpty();
        SelfTargets.Refusal(control, DeploymentEnvironment.Prod, derived.Network, derived.Root,
                [new TargetSettingValueObject { Service = "console", Path = derived.Settings[0].Path }], [], host: null)
            .Should().Be("This console's settings name no hostname (AllowedHosts lists none), so Wheelhouse cannot derive its console site.");
    }
}
