using System.Globalization;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentBuildFailuresQuery"/>: the runner reads each product's build workflow runs on its
/// default branch (kept two minutes); a product whose newest finished run failed is listed, newest first. A product the
/// inventory no longer holds is left out, and so is one GitHub could not serve.</summary>
public sealed class DeploymentBuildFailuresQueryHandler(
    IDeploymentGateway gateway, IProductsRepository products, IWorkspacesRepository workspaces)
    : IQueryHandler<DeploymentBuildFailuresQuery, AppResult<IReadOnlyList<BuildFailureDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<BuildFailureDto>>> HandleAsync(
        DeploymentBuildFailuresQuery request, CancellationToken cancellationToken)
    {
        var read = await gateway.ReadAsync("builds", null, cancellationToken);
        if (read is not AppResult<JsonElement>.Success { Data: var document })
            return AppResult<IReadOnlyList<BuildFailureDto>>.Fail(((AppResult<JsonElement>.Failure)read).Error);

        var workspaceSlugs = (await workspaces.GetAllAsync(cancellationToken)).ToDictionary(row => row.Id, row => row.Slug);
        var known = (await products.GetAllAsync(cancellationToken))
            .ToDictionary(row => row.Slug, row => workspaceSlugs.GetValueOrDefault(row.WorkspaceId), StringComparer.Ordinal);
        var filter = request.Filter;
        var result = new List<BuildFailureDto>();
        foreach (var item in document.ValueKind == JsonValueKind.Array ? document.EnumerateArray() : default)
        {
            if (Text(item, "product") is not { } product || !known.TryGetValue(product, out var workspace)
                || !item.TryGetProperty("failing", out var failing) || failing.ValueKind != JsonValueKind.Object
                || !failing.TryGetProperty("runId", out var run) || !run.TryGetInt64(out var runId)
                || Text(failing, "url") is not { } url || !url.StartsWith("https://github.com/", StringComparison.Ordinal))
                continue;
            if (filter.HasEntities && !filter.Products.Contains(product, StringComparer.Ordinal)
                && !(workspace is not null && filter.Workspaces.Contains(workspace, StringComparer.Ordinal))
                && !filter.Services.Any(service => service.StartsWith(product + "/", StringComparison.Ordinal)))
                continue;
            result.Add(new BuildFailureDto(product, workspace, runId, Text(failing, "conclusion") ?? "failure",
                Text(failing, "commit"), Text(failing, "title") ?? "", Instant(failing, "at"), url));
        }

        return AppResult<IReadOnlyList<BuildFailureDto>>.Ok([.. result.OrderByDescending(item => item.At)]);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Instant(JsonElement element, string property) =>
        Text(element, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var instant) ? instant : null;
}
