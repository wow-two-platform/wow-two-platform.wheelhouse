namespace Wheelhouse.Domain.Servers.Enums;

/// <summary>Refers to who hosts a server.</summary>
public enum VpsProvider
{
    /// <summary>A Hetzner Cloud virtual server.</summary>
    Hetzner,

    /// <summary>The local server Wheelhouse rehearses deployments on.</summary>
    Local,

    /// <summary>Refers to an OVHcloud virtual server.</summary>
    Ovhcloud,

    /// <summary>Refers to a Contabo virtual server.</summary>
    Contabo,
}
