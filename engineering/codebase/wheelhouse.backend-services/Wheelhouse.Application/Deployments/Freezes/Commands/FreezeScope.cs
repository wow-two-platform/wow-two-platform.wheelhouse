namespace Wheelhouse.Application.Deployments.Freezes.Commands;

/// <summary>Names a freeze's scope the way the audit trail records it.</summary>
public static class FreezeScope
{
    /// <summary>Gets <c>portfolio</c>, the product, or <c>{product}/{environment}</c>.</summary>
    /// <param name="product">The product, or null for every product.</param>
    /// <param name="environment">The environment, or null for all of the product's.</param>
    /// <returns>The subject.</returns>
    public static string Subject(string? product, string? environment) =>
        product is null ? "portfolio" : environment is null ? product : product + "/" + environment;
}
