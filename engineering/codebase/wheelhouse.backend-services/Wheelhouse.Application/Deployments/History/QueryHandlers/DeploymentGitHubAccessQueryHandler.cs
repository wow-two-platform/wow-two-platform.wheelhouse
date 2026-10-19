using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentGitHubAccessQuery"/> from what the console and the runner noted; GitHub
/// access is the whole control plane's, so no deployment filter applies.</summary>
public sealed class DeploymentGitHubAccessQueryHandler(IGitHubAccessIssues issues)
    : IQueryHandler<DeploymentGitHubAccessQuery, AppResult<IReadOnlyList<GitHubAccessIssueDto>>>
{
    /// <inheritdoc />
    public ValueTask<AppResult<IReadOnlyList<GitHubAccessIssueDto>>> HandleAsync(
        DeploymentGitHubAccessQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AppResult<IReadOnlyList<GitHubAccessIssueDto>>.Ok(issues.Current()));
}
