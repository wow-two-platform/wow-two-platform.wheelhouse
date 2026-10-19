using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the read of what keeps Wheelhouse from reaching GitHub: an owner without the GitHub App, a refused
/// credential, a spent rate limit, or an App set up by half. In-process and runner-noted; no GitHub call is made.</summary>
public interface IGitHubAccessIssues
{
    /// <summary>Lists the current problems, newest first; a later success for an owner clears its problem.</summary>
    /// <returns>The problems, each naming its owner and its cause in plain words.</returns>
    IReadOnlyList<GitHubAccessIssueDto> Current();
}
