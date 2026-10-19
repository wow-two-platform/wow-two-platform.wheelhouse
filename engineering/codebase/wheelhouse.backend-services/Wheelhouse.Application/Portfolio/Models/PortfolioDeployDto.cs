using Wheelhouse.Application.Deployments.History.Models;

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
/// <param name="State">What the environment runs because of it, in the status taxonomy.</param>
/// <param name="Substate">Which way it got there.</param>
/// <param name="Attention">How much attention it needs.</param>
/// <param name="ServicesOnRelease">How many of its services run the release it brought; null when the target did not
/// report its services.</param>
/// <param name="ServiceCount">How many services it reported.</param>
/// <param name="Watch">Its watch window's status after a success, such as <c>watching</c>; null when not watched.</param>
public sealed record PortfolioDeployDto(
    string Id, string? Release, string Status, string Outcome, string? Actor, DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt, string? Reason, string State = DeploymentState.Unknown, string? Substate = null,
    string Attention = DeploymentAttention.Muted, int? ServicesOnRelease = null, int? ServiceCount = null, string? Watch = null);
