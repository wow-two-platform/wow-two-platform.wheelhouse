namespace Wheelhouse.Application.Audit;

/// <summary>The subjects one product's audit entries name, by action family, so an audit read can keep only the
/// entries about that product. Each family matches its own subjects only: a server, vault or workspace that shares a
/// product's slug never pulls its entries in.</summary>
/// <param name="Product">The product's slug: the subject of <c>product.*</c> and <c>build.*</c> entries.</param>
/// <param name="Targets">The product's target slugs: the subjects of <c>deployment.*</c> and <c>target.*</c> entries.</param>
/// <param name="Namespaces">The product's vault paths, <c>vault/namespace</c>: the subjects of <c>vault.*</c> changes.</param>
public sealed record AuditProductScope(
    string Product, IReadOnlyCollection<string> Targets, IReadOnlyCollection<string> Namespaces)
{
    /// <summary>Gets the subject prefix of the product's <c>service.*</c> entries, whose subject is <c>product/service</c>.</summary>
    public string ServicePrefix => Product + "/";
}
