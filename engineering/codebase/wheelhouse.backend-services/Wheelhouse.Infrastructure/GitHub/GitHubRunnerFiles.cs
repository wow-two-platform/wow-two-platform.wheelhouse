namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Names the files the API and the runner share about GitHub access, under <c>{root}/github/</c>; the runner's
/// <c>artifacts.py</c> reads and writes the same.</summary>
public static class GitHubRunnerFiles
{
    /// <summary>Holds the folder under the runner root.</summary>
    public const string Folder = "github";

    /// <summary>Holds the installation tokens the API writes for the runner, per owner, mode 0600.</summary>
    public const string Tokens = "tokens.json";

    /// <summary>Holds the refused calls the runner notes, per owner, for the Issues panel.</summary>
    public const string Access = "access.json";

    /// <summary>Gets the tokens file under a runner root.</summary>
    /// <param name="root">The runner root.</param>
    public static string TokensPath(string root) => Path.Combine(root, Folder, Tokens);

    /// <summary>Gets the access-notes file under a runner root.</summary>
    /// <param name="root">The runner root.</param>
    public static string AccessPath(string root) => Path.Combine(root, Folder, Access);
}
