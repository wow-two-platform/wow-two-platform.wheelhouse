using System.Net;
using System.Net.Sockets;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Enums;

namespace Wheelhouse.Application.Servers.Services;

/// <summary>Tells the control host — the server the consoles themselves run on — from every other server: the consoles
/// reach their own host over a Docker bridge, at a private address (its <c>platform</c> address, <c>vps/network.md</c>),
/// while a products host is reached at its tailnet address and a fresh server at its public one. The local rig's server
/// is never the control host. Wheelhouse's own environments run only there, and it is never enrolled.</summary>
public static class ControlHost
{
    /// <summary>Whether a server reads as the control host.</summary>
    /// <param name="server">The stored server.</param>
    /// <returns><c>true</c> when it is not the local rig's and its host is a private IPv4 address.</returns>
    public static bool Is(ServerEntity server) => Is(server.Provider, server.Host);

    /// <summary>Whether a server with this provider and host reads as the control host.</summary>
    /// <param name="provider">Who hosts the server.</param>
    /// <param name="host">The host name or address SSH connects to.</param>
    /// <returns><c>true</c> when the provider is not the local rig and the host is a private IPv4 address
    /// (10.0.0.0/8, 172.16.0.0/12 or 192.168.0.0/16).</returns>
    public static bool Is(VpsProvider provider, string host)
    {
        if (provider == VpsProvider.Local || !IPAddress.TryParse(host, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork || host.Count(character => character == '.') != 3)
            return false;
        var octets = address.GetAddressBytes();
        return octets[0] == 10 || (octets[0] == 172 && octets[1] is >= 16 and <= 31) || (octets[0] == 192 && octets[1] == 168);
    }
}
