using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentPromotionsQuery"/>. Only a product Wheelhouse deploys is offered: one its own
/// CI or nothing deploys is left out, so neither Ready to promote, auto-promote nor a fleet takes it. Prod takes published
/// releases only, so a commit build that succeeded on dev is never offered; a prod rollout of the release already under way, or a prod already on it or on a
/// later release, leaves the product out.</summary>
public sealed class DeploymentPromotionsQueryHandler(DeploymentHistoryService history)
    : IQueryHandler<DeploymentPromotionsQuery, AppResult<IReadOnlyList<DeploymentPromotionDto>>>
{
    /// <summary>Holds the environment a release proves itself on.</summary>
    private const string Dev = "dev";

    /// <summary>Holds the environment a proven release is promoted to.</summary>
    private const string Prod = "prod";

    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<DeploymentPromotionDto>>> HandleAsync(
        DeploymentPromotionsQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<IReadOnlyList<DeploymentPromotionDto>>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var filter = request.Filter;
        var result = new List<DeploymentPromotionDto>();
        foreach (var product in loaded.Targets.GroupBy(target => target.Product, StringComparer.Ordinal))
        {
            var workspace = product.First().Workspace;
            if (product.First().Deploy != ProductDeployMode.Wheelhouse)
                continue;
            if (filter.HasEntities
                && !filter.Products.Contains(product.Key, StringComparer.Ordinal)
                && !(workspace is not null && filter.Workspaces.Contains(workspace, StringComparer.Ordinal))
                && !filter.Services.Any(service => service.StartsWith(product.Key + "/", StringComparison.Ordinal)))
                continue;

            var dev = product.Where(target => target.Environment == Dev).Select(target => target.Slug).ToHashSet(StringComparer.Ordinal);
            var proven = loaded.Entries
                .Where(entry => entry.Record.TargetId is { } target && dev.Contains(target)
                    && entry.Outcome == DeploymentOutcome.Succeeded && IsRelease(entry.Record)
                    && entry.Record.Release is not null && entry.Record.BundleId is not null)
                .OrderByDescending(entry => entry.Record.PublishedAt ?? entry.Record.CompletedAt ?? entry.Record.SubmittedAt)
                .FirstOrDefault();
            if (proven is null)
                continue;

            foreach (var prod in product.Where(target => target.Environment == Prod))
            {
                var onProd = loaded.Entries.Where(entry => entry.Record.TargetId == prod.Slug).ToList();
                var runs = onProd.Where(entry => entry.Outcome == DeploymentOutcome.Succeeded)
                    .MaxBy(entry => entry.Record.CompletedAt ?? entry.Record.SubmittedAt);
                if (runs?.Record.Release == proven.Record.Release
                    || (runs?.Record.PublishedAt is { } running && proven.Record.PublishedAt is { } offered && running >= offered)
                    || onProd.Any(entry => entry.Outcome == DeploymentOutcome.Pending && entry.Record.Release == proven.Record.Release))
                    continue;
                result.Add(new DeploymentPromotionDto(product.Key, workspace, proven.Record.Release!, proven.Record.BundleId!,
                    proven.Record.PublishedAt, proven.Record.TargetId!, proven.Record.CompletedAt ?? proven.Record.SubmittedAt,
                    prod.Slug, runs?.Record.Release, proven.Record.RollbackCompatible, proven.Record.Migrations,
                    proven.Record.Verifies, proven.Record.Verification));
            }
        }

        return AppResult<IReadOnlyList<DeploymentPromotionDto>>.Ok([.. result.OrderByDescending(item => item.DevSucceededAt)]);
    }

    /// <summary>Whether a rollout deployed a published release rather than a commit build. @internal</summary>
    private static bool IsRelease(DeploymentRecord record) =>
        record.Kind is "release" || (record.Kind is null && record.Release is { } release
            && !release.StartsWith("sha-", StringComparison.Ordinal));
}
