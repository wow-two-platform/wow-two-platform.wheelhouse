namespace Wheelhouse.Infrastructure.Products;

/// <summary>Signals that the runner's rollout records could not be read, so the cache keeps nothing and the next read
/// asks the runner again.</summary>
/// <param name="reason">Why the records could not be read.</param>
internal sealed class TargetSitesUnreadableException(string reason) : Exception(reason);
