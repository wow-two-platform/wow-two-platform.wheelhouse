using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Models;

namespace Wheelhouse.Application.Servers.Mappers;

/// <summary>Maps a stored server to the server the operator edits.</summary>
internal static class ServerMapper
{
    /// <summary>Maps a stored server to its projection.</summary>
    /// <param name="server">The stored server.</param>
    /// <returns>The server the operator edits.</returns>
    public static ServerDto Map(ServerEntity server) => new()
    {
        Slug = server.Slug,
        Name = server.Name,
        Provider = server.Provider,
        Host = server.Host,
        Region = server.Region,
        SshUser = server.SshUser,
        SshPort = server.SshPort,
        Ingress = Map(server.Ingress),
    };

    /// <summary>Maps a stored ingress to its projection.</summary>
    /// <param name="ingress">The stored ingress.</param>
    /// <returns>The ingress the operator edits.</returns>
    private static ServerIngressDto Map(ServerIngressValueObject ingress) => new()
    {
        Scheme = ingress.Scheme,
        Port = ingress.Port,
        EntryPoints = ingress.EntryPoints,
        PrivateEntryPoints = ingress.PrivateEntryPoints,
        CertResolver = ingress.CertResolver,
        Pattern = ingress.Pattern,
        Probe = ingress.Probe,
        PrivateProbe = ingress.PrivateProbe,
    };
}
