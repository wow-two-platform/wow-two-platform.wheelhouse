namespace Wheelhouse.Application.Deployments.SelfDeploy.Constants;

/// <summary>Holds how Wheelhouse knows itself in its own inventory: prod deploys each Wheelhouse release to its dev and
/// promotes itself (decision 64).</summary>
public static class SelfProduct
{
    /// <summary>Holds Wheelhouse's product slug; its targets are the consoles <c>wheelhouse-dev</c> and
    /// <c>wheelhouse-prod</c>.</summary>
    public const string Slug = "wheelhouse";

    /// <summary>Holds the Docker network both consoles share with PostgreSQL and Traefik on the control host
    /// (<c>vps/network.md</c>); products run on <c>platform</c>.</summary>
    public const string Network = "control";

    /// <summary>Holds the folder the control host's kit lays out for the consoles (<c>vps/prepare.py</c>).</summary>
    public const string Root = "/srv/wheelhouse";

    /// <summary>Holds the console's service key in Wheelhouse's <c>deploy.yml</c>.</summary>
    public const string Service = "console";

    /// <summary>Holds the console's private site in Wheelhouse's <c>deploy.yml</c>.</summary>
    public const string Site = "console";

    /// <summary>Gets the host settings file the kit writes for a console: <c>/srv/wheelhouse/config/console-dev.json</c>.</summary>
    /// <param name="environment">The console's environment, <c>dev</c> or <c>prod</c>.</param>
    /// <returns>The file's absolute path on the control host.</returns>
    public static string SettingsPath(string environment) => Root + "/config/console-" + environment + ".json";
}
