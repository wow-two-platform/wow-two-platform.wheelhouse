namespace Wheelhouse.Api.Requests;

/// <summary>Names the scope a freeze holds and why.</summary>
/// <param name="Product">The product's id or slug; omitted freezes every product.</param>
/// <param name="Environment"><c>dev</c> or <c>prod</c> to freeze one environment of the product.</param>
/// <param name="Reason">Why, as the owner writes it; one line, 300 characters at most.</param>
public sealed record DeployFreezeRequest(string? Product = null, string? Environment = null, string? Reason = null);
