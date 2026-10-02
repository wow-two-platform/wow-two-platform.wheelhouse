using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Models;
using Wheelhouse.Domain.Vaults.Entities;
using Wheelhouse.Infrastructure.Inventory.Models;

namespace Wheelhouse.Infrastructure.Inventory.Mappers;

/// <summary>Maps stored products, servers, targets and vaults to the runner's inventory snapshot and back.</summary>
internal static class InventorySnapshotMapper
{
    /// <summary>Maps a stored product to its snapshot entry.</summary>
    /// <param name="product">The stored product.</param>
    /// <returns>The product as the runner reads it.</returns>
    public static SnapshotProductModel Map(ProductEntity product) => new()
    {
        Slug = product.Slug,
        Name = product.Name,
        Description = product.Description,
        Repository = product.Repository,
        DefaultBranch = product.DefaultBranch,
        Release = product.Release is { } release
            ? new SnapshotReleaseModel
            {
                Asset = release.Asset,
                Workflow = release.Workflow,
                Images = [.. release.Images.Select(image => new SnapshotImageModel { Service = image.Service, Image = image.Image })],
            }
            : null,
    };

    /// <summary>Maps a stored server to its snapshot entry.</summary>
    /// <param name="server">The stored server.</param>
    /// <returns>The server as the runner reads it.</returns>
    public static SnapshotServerModel Map(ServerEntity server) => new()
    {
        Id = server.Slug,
        Name = server.Name,
        Provider = server.Provider,
        Host = server.Host,
        Region = server.Region,
        SshUser = server.SshUser,
        SshPort = server.SshPort,
        Ingress = new SnapshotIngressModel
        {
            Scheme = server.Ingress.Scheme,
            Port = server.Ingress.Port,
            EntryPoints = server.Ingress.EntryPoints,
            PrivateEntryPoints = server.Ingress.PrivateEntryPoints,
            CertResolver = server.Ingress.CertResolver,
            Pattern = server.Ingress.Pattern,
            Probe = server.Ingress.Probe,
            PrivateProbe = server.Ingress.PrivateProbe,
        },
    };

    /// <summary>Maps a stored target to its snapshot entry.</summary>
    /// <param name="target">The stored target.</param>
    /// <param name="product">The slug of the target's product.</param>
    /// <param name="server">The slug of the target's server.</param>
    /// <returns>The target as the runner reads it.</returns>
    public static SnapshotTargetModel Map(TargetEntity target, string product, string server) => new()
    {
        Id = target.Slug,
        ServerId = server,
        Product = product,
        Environment = target.Environment,
        Network = target.Network,
        Root = target.Root,
        Settings = [.. target.Settings.Select(setting => new SnapshotSettingModel { Service = setting.Service, Path = setting.Path })],
        Smoke = [.. target.SmokeChecks.Select(check => new SnapshotSmokeModel { Service = check.Service, Path = check.Path, Status = check.Status })],
        Sites = [.. target.Sites.Select(site => new SnapshotSiteModel { Site = site.Site, Host = site.Host })],
    };

    /// <summary>Maps a stored vault to its snapshot entry.</summary>
    /// <param name="vault">The stored vault.</param>
    /// <param name="server">The slug of the vault's server.</param>
    /// <returns>The vault as the runner reads it.</returns>
    public static SnapshotVaultModel Map(VaultEntity vault, string server) => new()
    {
        Id = vault.Slug,
        Name = vault.Name,
        ServerId = server,
        Url = vault.Url,
    };

    /// <summary>Maps a snapshot release source to the stored one.</summary>
    /// <param name="release">The release source as the runner defines it, or <c>null</c>.</param>
    /// <returns>The stored release source, or <c>null</c>.</returns>
    public static ProductReleaseValueObject? Map(SnapshotReleaseModel? release) => release is null
        ? null
        : new ProductReleaseValueObject
        {
            Asset = release.Asset,
            Workflow = release.Workflow,
            Images = [.. release.Images.Select(image => new ReleaseImageValueObject { Service = image.Service, Image = image.Image })],
        };

    /// <summary>Maps a snapshot ingress to the stored one.</summary>
    /// <param name="ingress">The ingress as the runner defines it.</param>
    /// <returns>The stored ingress.</returns>
    public static ServerIngressValueObject Map(SnapshotIngressModel ingress) => new()
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

    /// <summary>Maps snapshot settings files to the stored ones.</summary>
    /// <param name="settings">The settings files as the runner defines them.</param>
    /// <returns>The stored settings files.</returns>
    public static IReadOnlyList<TargetSettingValueObject> Map(IReadOnlyList<SnapshotSettingModel> settings) =>
        [.. settings.Select(setting => new TargetSettingValueObject { Service = setting.Service, Path = setting.Path })];

    /// <summary>Maps snapshot smoke checks to the stored ones.</summary>
    /// <param name="checks">The smoke checks as the runner defines them.</param>
    /// <returns>The stored smoke checks.</returns>
    public static IReadOnlyList<SmokeCheckValueObject> Map(IReadOnlyList<SnapshotSmokeModel> checks) =>
        [.. checks.Select(check => new SmokeCheckValueObject { Service = check.Service, Path = check.Path, Status = check.Status })];

    /// <summary>Maps snapshot site hosts to the stored ones.</summary>
    /// <param name="sites">The site hosts as the runner defines them.</param>
    /// <returns>The stored site hosts.</returns>
    public static IReadOnlyList<SiteHostValueObject> Map(IReadOnlyList<SnapshotSiteModel> sites) =>
        [.. sites.Select(site => new SiteHostValueObject { Site = site.Site, Host = site.Host })];
}
