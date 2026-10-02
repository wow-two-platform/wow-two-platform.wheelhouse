namespace Wheelhouse.Application.Inventory.Constants;

/// <summary>Holds the shapes inventory names, addresses and paths must take — the same ones the runner enforces.</summary>
public static class InventoryPatternConstants
{
    /// <summary>Holds the shape of a product, server, target, vault, service or site name.</summary>
    public const string Slug = "^[a-z][a-z0-9-]{0,47}$";

    /// <summary>Holds the shape of a GitHub repository, as <c>owner/name</c>.</summary>
    public const string Repository = "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$";

    /// <summary>Holds the shape of a Git branch name.</summary>
    public const string Branch = "^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$";

    /// <summary>Holds the shape of a release asset's file name.</summary>
    public const string Asset = "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$";

    /// <summary>Holds the shape of a GitHub Actions workflow file name.</summary>
    public const string Workflow = @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.ya?ml$";

    /// <summary>Holds the shape of an image repository, such as <c>ghcr.io/owner/product/api</c>.</summary>
    public const string Image = "^[a-z0-9][a-z0-9.-]*(?::[0-9]{1,5})?(?:/[a-z0-9][a-z0-9._-]*)+$";

    /// <summary>Holds the shape of a site's host name: lowercase labels, at least two.</summary>
    public const string SiteHost =
        @"^(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$";

    /// <summary>Holds the shape of the host name or address SSH connects to.</summary>
    public const string SshHost = "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$";

    /// <summary>Holds the shape of the user SSH signs in as.</summary>
    public const string SshUser = "^[a-z_][a-z0-9_-]{0,31}$";

    /// <summary>Holds the shape of a provider region, such as <c>hel1</c>.</summary>
    public const string Region = "^[a-z0-9][a-z0-9-]{0,31}$";

    /// <summary>Holds the shape of a Traefik entry point or certificate resolver name.</summary>
    public const string EntryPoint = "^[a-z][a-z0-9-]{0,31}$";

    /// <summary>Holds the shape of an address the runner reaches an ingress or a vault at: a scheme, a host and an
    /// optional port.</summary>
    public const string Endpoint = "^https?://[A-Za-z0-9.-]+(?::[0-9]{1,5})?$";

    /// <summary>Holds the shape of a Docker network name.</summary>
    public const string Network = "^[A-Za-z0-9][A-Za-z0-9_.-]{0,62}$";

    /// <summary>Holds the shape of a path a smoke check requests.</summary>
    public const string SmokePath = "^/[A-Za-z0-9/_?=&.%~-]{0,199}$";
}
