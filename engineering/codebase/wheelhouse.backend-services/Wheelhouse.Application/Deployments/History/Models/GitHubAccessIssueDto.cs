namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one thing keeping Wheelhouse from reaching GitHub; never carries a token, key or response body.</summary>
/// <param name="Owner">The GitHub account it concerns, or <c>null</c> for the GitHub App's own setup.</param>
/// <param name="Cause">What went wrong and what follows, in plain words.</param>
/// <param name="Status">The HTTP status GitHub answered, when one did.</param>
/// <param name="At">When it was last seen.</param>
public sealed record GitHubAccessIssueDto(string? Owner, string Cause, int? Status, DateTimeOffset At);
