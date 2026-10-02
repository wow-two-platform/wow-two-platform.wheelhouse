using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Infrastructure.Products;

/// <summary>Provides each target's published sites from the runner's rollout records, held briefly so an integration
/// that polls costs no runner process per request.</summary>
/// <remarks>The records are the runner's until rollouts land in the database; an unreadable runner yields no sites,
/// so a product read never fails on them.</remarks>
public sealed partial class RunnerTargetSites(IDeploymentGateway gateway, IMemoryCache cache, ILogger<RunnerTargetSites> logger)
    : ITargetSites
{
    /// <summary>Holds the cache key of the last read.</summary>
    private const string CacheKey = "target-sites";

    /// <summary>Holds how long a read serves; a new rollout's sites show within it.</summary>
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>>> ReadAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>>? cached) && cached is not null)
            return cached;

        var read = await gateway.ReadAsync("sites", null, ct);
        if (read is not AppResult<JsonElement>.Success { Data: var document })
        {
            LogUnreadable(logger, ((AppResult<JsonElement>.Failure)read).Error.Message);
            return new Dictionary<string, IReadOnlyList<ProductSiteModel>>();
        }

        try
        {
            IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>> sites =
                document.Deserialize<Dictionary<string, List<ProductSiteModel>>>(JsonSerializerOptions.Web)?
                    .ToDictionary(entry => entry.Key, entry => (IReadOnlyList<ProductSiteModel>)entry.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, IReadOnlyList<ProductSiteModel>>();
            cache.Set(CacheKey, sites, FreshFor);
            return sites;
        }
        catch (JsonException)
        {
            LogUnreadable(logger, "unreadable sites");
            return new Dictionary<string, IReadOnlyList<ProductSiteModel>>();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Published sites could not be read from the runner ({Reason}).")]
    private static partial void LogUnreadable(ILogger logger, string reason);
}
