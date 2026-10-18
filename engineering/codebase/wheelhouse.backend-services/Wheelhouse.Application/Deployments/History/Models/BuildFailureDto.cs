namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a product whose newest finished main build failed; a later success clears it.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Workspace">The product's workspace slug.</param>
/// <param name="RunId">The GitHub Actions run.</param>
/// <param name="Conclusion">How it ended: <c>failure</c>, <c>timed_out</c> or <c>startup_failure</c>.</param>
/// <param name="Commit">The commit it built.</param>
/// <param name="Title">The run's title, usually the commit's subject.</param>
/// <param name="At">When it ended.</param>
/// <param name="Url">The run on GitHub.</param>
public sealed record BuildFailureDto(
    string Product, string? Workspace, long RunId, string Conclusion, string? Commit, string Title, DateTimeOffset? At, string Url);
