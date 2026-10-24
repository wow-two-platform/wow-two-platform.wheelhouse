using Wheelhouse.Application.Deployments.SelfDeploy.Constants;
using Wheelhouse.Application.Servers.Services;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;

namespace Wheelhouse.Application.Targets.Services;

/// <summary>Derives Wheelhouse's own environments, <c>wheelhouse-dev</c> and <c>wheelhouse-prod</c> (decision 64), and
/// refuses one saved otherwise, so neither the form nor an API client types them: both run on the control host
/// (<see cref="ControlHost"/>) on the <c>control</c> network under <c>/srv/wheelhouse</c>, read the console settings file
/// the kit wrote (<c>config/console-&lt;env&gt;.json</c>) and publish the console site on this console's own hostname.
/// Both share that hostname; the runner gives dev its own entry point and port (<c>fleet.py</c> <c>CONSOLE_INGRESS</c>).
/// Smoke checks stay the owner's.</summary>
public static class SelfTargets
{
    /// <summary>Whether a product is Wheelhouse itself.</summary>
    /// <param name="product">The product's slug.</param>
    /// <returns><c>true</c> for <see cref="SelfProduct.Slug"/>.</returns>
    public static bool IsSelf(string product) => string.Equals(product, SelfProduct.Slug, StringComparison.Ordinal);

    /// <summary>Derives everything Wheelhouse knows about itself for its own environments.</summary>
    /// <param name="host">This console's hostname, or <c>null</c> when its settings name none.</param>
    /// <returns>Each environment's definition, in promotion order.</returns>
    public static SelfTargetDto Derive(string? host) => new()
    {
        Product = SelfProduct.Slug,
        Host = host,
        Environments = [Derive(DeploymentEnvironment.Dev, host), Derive(DeploymentEnvironment.Prod, host)],
    };

    /// <summary>Derives one of Wheelhouse's own environments.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="host">This console's hostname, or <c>null</c> when its settings name none.</param>
    /// <returns>The environment's definition; no site while the hostname is unknown.</returns>
    public static SelfTargetEnvironmentDto Derive(DeploymentEnvironment environment, string? host) => new()
    {
        Environment = environment,
        Network = SelfProduct.Network,
        Root = SelfProduct.Root,
        Settings = [new TargetSettingDto { Service = SelfProduct.Service, Path = SelfProduct.SettingsPath(Name(environment)) }],
        Sites = host is null ? [] : [new SiteHostDto { Site = SelfProduct.Site, Host = host }],
    };

    /// <summary>Says why a definition of one of Wheelhouse's own environments cannot be saved, or <c>null</c> when it is
    /// exactly what Wheelhouse derives.</summary>
    /// <param name="server">The server the environment would run on.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="network">The requested network.</param>
    /// <param name="root">The requested folder.</param>
    /// <param name="settings">The requested settings files.</param>
    /// <param name="sites">The requested site hosts.</param>
    /// <param name="host">This console's hostname, or <c>null</c> when its settings name none.</param>
    /// <returns>A display-safe refusal naming what Wheelhouse derives, or <c>null</c>.</returns>
    public static string? Refusal(
        ServerEntity server, DeploymentEnvironment environment, string network, string root,
        IReadOnlyList<TargetSettingValueObject> settings, IReadOnlyList<SiteHostValueObject> sites, string? host)
    {
        if (!ControlHost.Is(server))
            return $"Wheelhouse's own environments run on the control host, the server reached at its private platform address; '{server.Slug}' is not it.";
        if (host is null)
            return "This console's settings name no hostname (AllowedHosts lists none), so Wheelhouse cannot derive its console site.";

        var expected = Derive(environment, host);
        var setting = expected.Settings[0];
        var site = expected.Sites[0];
        var differs = new List<string>();
        if (!string.Equals(network, expected.Network, StringComparison.Ordinal))
            differs.Add($"network '{expected.Network}'");
        if (!string.Equals(root, expected.Root, StringComparison.Ordinal))
            differs.Add($"folder '{expected.Root}'");
        if (settings.Count != 1 || settings[0].Service != setting.Service || settings[0].Path != setting.Path)
            differs.Add($"settings file {setting.Service} → {setting.Path}");
        if (sites.Count != 1 || sites[0].Site != site.Site || sites[0].Host != site.Host)
            differs.Add($"site {site.Site} → {site.Host}");
        return differs.Count == 0
            ? null
            : "Wheelhouse derives its own environments; this one takes " + string.Join(", ", differs) + " and nothing else.";
    }

    private static string Name(DeploymentEnvironment environment) => environment.ToString().ToLowerInvariant();
}
