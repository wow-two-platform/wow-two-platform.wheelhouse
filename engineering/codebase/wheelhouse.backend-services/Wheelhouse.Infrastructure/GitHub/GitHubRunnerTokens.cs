using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Writes the GitHub App's installation tokens for the runner, one per owner of an inventory product, to
/// <c>{root}/github/tokens.json</c> (mode 0600): <c>{"owners": {owner: {"token", "expiresAt"}}}</c>. The runner picks a
/// token by the owner in each API address and falls back to the token file, then anonymous.</summary>
/// <remarks>Written before every runner call that reaches GitHub; a token is replaced <see cref="GitHubAppTokens.RefreshBefore"/>
/// its expiry, so one call always finishes inside it. The file is rewritten only when a token changed.</remarks>
public sealed partial class GitHubRunnerTokens(
    GitHubAppTokens app,
    IProductsRepository products,
    DeploymentSettings settings,
    ILogger<GitHubRunnerTokens> logger)
{
    /// <summary>Holds the turn every write in this process waits for.</summary>
    private static readonly SemaphoreSlim Turn = new(1, 1);

    /// <summary>Brings the tokens file up to date.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The file's path for the runner, or <c>null</c> when the App is off or the file could not be written.</returns>
    public async Task<string?> PrepareAsync(CancellationToken ct)
    {
        if (!app.IsConfigured || settings.Root.Length == 0)
            return null;

        var owners = (await products.GetAllAsync(ct))
            .Select(product => GitHubOwners.FromRepository(product.Repository))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var tokens = new SortedDictionary<string, RunnerToken>(StringComparer.Ordinal);
        foreach (var owner in owners)
        {
            if (await app.GetAsync(owner, ct) is { } token)
                tokens[owner] = new RunnerToken(
                    token.Token, token.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        }

        var path = GitHubRunnerFiles.TokensPath(settings.Root);
        var content = JsonSerializer.Serialize(new RunnerTokens(tokens));
        await Turn.WaitAsync(ct);
        try
        {
            if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) == content)
                return path;
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            else
                Directory.CreateDirectory(Path.GetDirectoryName(path)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(content.AsMemory(), ct);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnwritten(logger, exception.GetType().Name);
            return File.Exists(path) ? path : null;
        }
        finally
        {
            Turn.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The runner's GitHub tokens file could not be written ({Reason}); the runner falls back to the token file or anonymous access.")]
    private static partial void LogUnwritten(ILogger logger, string reason);

    private sealed record RunnerTokens([property: JsonPropertyName("owners")] SortedDictionary<string, RunnerToken> Owners);

    private sealed record RunnerToken(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expiresAt")] string ExpiresAt);
}
