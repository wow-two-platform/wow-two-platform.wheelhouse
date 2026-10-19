namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Holds how much attention a deployment needs, which its colour follows; the wire carries these lowercase
/// names.</summary>
public static class DeploymentAttention
{
    /// <summary>Nothing to do.</summary>
    public const string None = "none";

    /// <summary>Something is in progress.</summary>
    public const string Info = "info";

    /// <summary>Serving, but worth a look.</summary>
    public const string Look = "look";

    /// <summary>Needs the owner's action.</summary>
    public const string Act = "act";

    /// <summary>Nothing is known.</summary>
    public const string Muted = "muted";

    /// <summary>Ranks an attention from calm to urgent, for the worst of several.</summary>
    /// <param name="attention">The attention.</param>
    /// <returns>0 for none or muted, 1 for info, 2 for look, 3 for act.</returns>
    public static int Rank(string attention) => attention switch { Act => 3, Look => 2, Info => 1, _ => 0 };
}
