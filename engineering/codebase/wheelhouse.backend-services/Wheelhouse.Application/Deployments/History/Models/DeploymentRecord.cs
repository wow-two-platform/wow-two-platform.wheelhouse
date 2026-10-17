namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one submission as the runner's history projection reports it: the submission, its last observed
/// outcome, what its cached bundle declares and, while it is live, the step it is on.</summary>
/// <remarks>The runner owns these facts in its local files; the API joins them with the inventory and never stores
/// them. A bundle missing from the runner's cache leaves <see cref="Product"/> and <see cref="Services"/> null.</remarks>
public sealed record DeploymentRecord
{
    /// <summary>Gets the submission's identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the target the release was submitted to.</summary>
    public string? TargetId { get; init; }

    /// <summary>Gets the release bundle's identifier in the catalog, such as <c>foreverpin-gh-42</c>.</summary>
    public string? BundleId { get; init; }

    /// <summary>Gets the release label, such as <c>v1.2.0</c> or <c>sha-abc1234</c>.</summary>
    public string? Release { get; init; }

    /// <summary>Gets the product slug the bundle names.</summary>
    public string? Product { get; init; }

    /// <summary>Gets <c>release</c> or <c>candidate</c> (a commit build).</summary>
    public string? Kind { get; init; }

    /// <summary>Gets the branch a commit build came from.</summary>
    public string? Branch { get; init; }

    /// <summary>Gets the release's full source commit.</summary>
    public string? SourceCommit { get; init; }

    /// <summary>Gets who submitted it.</summary>
    public string? Actor { get; init; }

    /// <summary>Gets the last observed status, such as <c>running</c>, <c>succeeded</c> or <c>rolled_back</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the operator-safe reason a rollout failed or was refused.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the failure category.</summary>
    public string? Failure { get; init; }

    /// <summary>Gets when the release was submitted.</summary>
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>Gets when the target's worker started the rollout.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets when the rollout finished.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Gets whether the rollout had replaced containers when it stopped.</summary>
    public bool? MutationStarted { get; init; }

    /// <summary>Gets what needs a look although the rollout succeeded.</summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>Gets each service's image and version in the bundle, by service key; null when the bundle is unknown.</summary>
    public IReadOnlyDictionary<string, DeploymentServiceRecord>? Services { get; init; }

    /// <summary>Gets whether the release declares that the previous release's images run on its schema.</summary>
    public bool? RollbackCompatible { get; init; }

    /// <summary>Gets when the release source published the bundle.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Gets the step a live rollout is on; null once it finished or before its worker started.</summary>
    public DeploymentStepRecord? CurrentStep { get; init; }
}
