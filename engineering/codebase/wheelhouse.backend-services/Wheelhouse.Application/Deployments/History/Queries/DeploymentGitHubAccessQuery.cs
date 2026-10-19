using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Lists what keeps Wheelhouse from reaching GitHub, per owner.</summary>
public sealed record DeploymentGitHubAccessQuery : IQuery<AppResult<IReadOnlyList<GitHubAccessIssueDto>>>;
