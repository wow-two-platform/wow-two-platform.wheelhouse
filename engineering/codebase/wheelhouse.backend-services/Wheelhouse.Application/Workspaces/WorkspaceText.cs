namespace Wheelhouse.Application.Workspaces;

/// <summary>Normalizes the free text a workspace carries.</summary>
internal static class WorkspaceText
{
    /// <summary>Trims a description; blank text means none.</summary>
    /// <param name="description">The description as given.</param>
    /// <returns>The trimmed description, or <c>null</c> when it is blank.</returns>
    public static string? Description(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
