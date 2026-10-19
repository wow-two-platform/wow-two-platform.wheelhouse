using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of what the control plane deploys on its own: each product's automation choices, the
/// release watcher's watermarks, the per-target queue of automatic deploys and the deploys that never reached their
/// target (refused, replaced in the queue, cancelled, expired or aborted).</summary>
public interface IDeploymentAutomationStore
{
    /// <summary>Gets every product's recorded automation choices; a product without a row takes the defaults.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The recorded rows.</returns>
    Task<IReadOnlyList<ProductAutomationEntity>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Records a product's automation choices, adding its row or changing it; a null choice keeps its value.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="autoDeployDev">Whether each new release deploys to dev, or null to keep it.</param>
    /// <param name="autoPromote">Whether a release that passed its dev watch is promoted to prod, or null to keep it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product's row as stored.</returns>
    Task<ProductAutomationEntity> SetSettingsAsync(Guid productId, bool? autoDeployDev, bool? autoPromote, CancellationToken ct = default);

    /// <summary>Gets every product's release watermark.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The watermarks.</returns>
    Task<IReadOnlyList<ReleaseWatermarkEntity>> GetWatermarksAsync(CancellationToken ct = default);

    /// <summary>Records a product's release watermark, adding its row or replacing it.</summary>
    /// <param name="watermark">The watermark.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetWatermarkAsync(ReleaseWatermarkEntity watermark, CancellationToken ct = default);

    /// <summary>Gets every queued automatic deploy, oldest first.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The queue.</returns>
    Task<IReadOnlyList<QueuedDeploymentEntity>> GetQueueAsync(CancellationToken ct = default);

    /// <summary>Puts a deploy in its target's slot, replacing whatever waited there.</summary>
    /// <param name="item">The deploy.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetQueuedAsync(QueuedDeploymentEntity item, CancellationToken ct = default);

    /// <summary>Gets the release auto-promote last tried on each prod target.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The marks.</returns>
    Task<IReadOnlyList<AutoPromotionEntity>> GetPromotionsAsync(CancellationToken ct = default);

    /// <summary>Records that auto-promote tried a release on a prod target.</summary>
    /// <param name="mark">The mark.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetPromotionAsync(AutoPromotionEntity mark, CancellationToken ct = default);

    /// <summary>Empties a target's slot.</summary>
    /// <param name="targetId">The target.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveQueuedAsync(Guid targetId, CancellationToken ct = default);

    /// <summary>Records a deploy that never reached its target, for the history to list as Not applied.</summary>
    /// <param name="intent">The deploy and how it ended.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddIntentAsync(DeploymentIntentEntity intent, CancellationToken ct = default);

    /// <summary>Gets every recorded deploy that never reached its target, oldest first.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The intents.</returns>
    Task<IReadOnlyList<DeploymentIntentEntity>> GetIntentsAsync(CancellationToken ct = default);
}
