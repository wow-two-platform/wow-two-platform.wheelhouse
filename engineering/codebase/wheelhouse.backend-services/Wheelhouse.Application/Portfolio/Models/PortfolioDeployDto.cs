namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents one rollout as a product card's environment pill describes it on hover.</summary>
/// <param name="Id">The submission's identifier.</param>
/// <param name="Release">The release label, such as <c>v1.2.0</c> or <c>sha-abc1234</c>.</param>
/// <param name="Status">The runner's last observed status, such as <c>succeeded</c> or <c>rolled_back</c>.</param>
/// <param name="Outcome">What the status amounts to: <c>succeeded</c>, <c>failed</c>, <c>pending</c> or <c>unknown</c>.</param>
/// <param name="Actor">Who submitted it.</param>
/// <param name="SubmittedAt">When it was submitted.</param>
/// <param name="CompletedAt">When it finished; null while it runs.</param>
/// <param name="Reason">The display-safe reason a rollout failed.</param>
public sealed record PortfolioDeployDto(
    string Id, string? Release, string Status, string Outcome, string? Actor, DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt, string? Reason);
