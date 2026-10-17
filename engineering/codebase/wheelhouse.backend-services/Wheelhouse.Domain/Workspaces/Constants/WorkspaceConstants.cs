namespace Wheelhouse.Domain.Workspaces.Constants;

/// <summary>Holds the workspace a product lands in when nothing names another.</summary>
public static class WorkspaceConstants
{
    /// <summary>Holds the slug of the default workspace; the schema creates it, and it comes back when a product needs
    /// it after it was removed.</summary>
    public const string DefaultSlug = "own-ventures";

    /// <summary>Holds the default workspace's display name.</summary>
    public const string DefaultName = "Own ventures";
}
