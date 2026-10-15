using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Backend.Beta.Caching.Core;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Provides each target's published sites from the runner's rollout records, held briefly so an integration
/// that polls costs no runner process per request.</summary>
/// <remarks>The records are the runner's until rollouts land in the database; an unreadable runner yields no sites,
/// so a product read never fails on them, and the next read asks the runner again.</remarks>
public sealed partial class RunnerTargetSites(IDeploymentGateway gateway, ICacheRepository cache, ILogger<RunnerTargetSites> logger)
    : ITargetSites
{
    /// <summary>Holds the cache key of the last read.</summary>
    private const string CacheKey = "target-sites";

    /// <summary>Holds how long a read serves; a new rollout's sites show within it.</summary>
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);

    /// <summary>Holds the cache entry lifetime, in-process and overall.</summary>
    private static readonly CacheEntryOptions Entry = new() { Expiration = FreshFor, LocalCacheExpiration = FreshFor };

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>>> ReadAsync(CancellationToken ct)
    {
        try
        {
            return await cache.GetOrCreateAsync(CacheKey, ReadRunnerAsync, Entry, ct);
        }
        catch (TargetSitesUnreadableException unreadable)
        {
            LogUnreadable(logger, unreadable.Message);
            return new Dictionary<string, IReadOnlyList<ProductSiteModel>>();
        }
    }

    /// <summary>Reads the sites from the runner; throws when they cannot be read, so the failure is never cached.</summary>
    private async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>>> ReadRunnerAsync(CancellationToken ct)
    {
        var read = await gateway.ReadAsync("sites", null, ct);
        if (read is not AppResult<JsonElement>.Success { Data: var document })
            throw new TargetSitesUnreadableException(((AppResult<JsonElement>.Failure)read).Error.Message);

        try
        {
            return document.Deserialize<Dictionary<string, List<ProductSiteModel>>>(JsonSerializerOptions.Web)?
                    .ToDictionary(entry => entry.Key, entry => (IReadOnlyList<ProductSiteModel>)entry.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, IReadOnlyList<ProductSiteModel>>();
        }
        catch (JsonException)
        {
            throw new TargetSitesUnreadableException("unreadable sites");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Published sites could not be read from the runner ({Reason}).")]
    private static partial void LogUnreadable(ILogger logger, string reason);
}
