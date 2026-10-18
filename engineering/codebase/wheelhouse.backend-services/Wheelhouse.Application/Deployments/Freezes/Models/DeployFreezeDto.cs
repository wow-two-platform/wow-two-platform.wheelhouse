namespace Wheelhouse.Application.Deployments.Freezes.Models;

/// <summary>Represents a hold on deploys.</summary>
/// <param name="Id">The freeze's identifier.</param>
/// <param name="Scope">What it covers: <c>portfolio</c>, <c>product</c> or <c>environment</c>.</param>
/// <param name="Product">The frozen product's slug; null for the portfolio.</param>
/// <param name="Environment">The frozen environment of the product; null for all of them.</param>
/// <param name="Reason">Why it was frozen.</param>
/// <param name="Actor">Who froze it.</param>
/// <param name="At">When it was frozen.</param>
public sealed record DeployFreezeDto(
    Guid Id, string Scope, string? Product, string? Environment, string? Reason, string Actor, DateTimeOffset At)
{
    /// <summary>Gets what it covers in words: <c>every product</c>, <c>foreverpin</c> or <c>foreverpin prod</c>.</summary>
    public string Covers => Product is null ? "every product" : Environment is null ? Product : Product + " " + Environment;
}
