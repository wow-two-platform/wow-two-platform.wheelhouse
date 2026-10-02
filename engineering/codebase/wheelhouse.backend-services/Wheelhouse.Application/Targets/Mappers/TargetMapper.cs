using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Application.Targets.Mappers;

/// <summary>Maps a stored target to the target the operator edits.</summary>
internal static class TargetMapper
{
    /// <summary>Maps a stored target to its projection, naming its product and server by slug.</summary>
    /// <param name="target">The stored target.</param>
    /// <param name="product">The slug of the target's product.</param>
    /// <param name="server">The slug of the target's server.</param>
    /// <returns>The target the operator edits.</returns>
    public static TargetDto Map(TargetEntity target, string product, string server) => new()
    {
        Slug = target.Slug,
        Product = product,
        Server = server,
        Environment = target.Environment,
        Network = target.Network,
        Root = target.Root,
        Settings = [.. target.Settings.Select(setting => new TargetSettingDto { Service = setting.Service, Path = setting.Path })],
        SmokeChecks =
        [
            .. target.SmokeChecks.Select(check => new SmokeCheckDto { Service = check.Service, Path = check.Path, Status = check.Status }),
        ],
        Sites = [.. target.Sites.Select(site => new SiteHostDto { Site = site.Site, Host = site.Host })],
    };
}
