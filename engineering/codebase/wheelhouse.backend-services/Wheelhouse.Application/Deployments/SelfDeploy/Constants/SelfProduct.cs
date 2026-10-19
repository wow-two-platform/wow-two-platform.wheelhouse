namespace Wheelhouse.Application.Deployments.SelfDeploy.Constants;

/// <summary>Holds how Wheelhouse knows itself in its own inventory: prod deploys each Wheelhouse release to its dev and
/// promotes itself (decision 64).</summary>
public static class SelfProduct
{
    /// <summary>Holds Wheelhouse's product slug; its targets are the consoles <c>wheelhouse-dev</c> and
    /// <c>wheelhouse-prod</c>.</summary>
    public const string Slug = "wheelhouse";
}
