using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.CommandHandlers;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Api.Cli;

/// <summary>The console's vault commands, run inside its container where the root key and the database are mounted:
/// <c>vault status</c>, <c>vault verify</c> (the restore proof), <c>vault import --from-vault &lt;slug&gt;</c> and
/// <c>vault import --from-db [--source-key-file &lt;path&gt;]</c>, which reads the standalone vault's connection string
/// from stdin. Prints JSON counts, never a value; each run is audited as <c>cli</c>.</summary>
public static class VaultCli
{
    /// <summary>The actor a command line run records.</summary>
    public const string Actor = "cli";

    /// <summary>Whether the process arguments name a vault command rather than the web host.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns><c>true</c> for <c>vault …</c>.</returns>
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == "vault";

    /// <summary>Runs a vault command against the built host, without starting it.</summary>
    /// <param name="app">The built application.</param>
    /// <param name="args">The process arguments, <c>vault</c> first.</param>
    /// <returns>The exit code: 0 done, 1 a refusal or a failed check, 2 a usage error.</returns>
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        await app.Services.MigrateBespokeOnStartupAsync();
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundActor>().Actor = Actor;
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        switch (args.Skip(1).ToArray())
        {
            case ["status"]:
                return Print(await sender.SendAsync(new VaultStatusQuery()), _ => true);
            case ["verify"]:
                return Print(await sender.SendAsync(new VaultVerifyCommand()),
                    result => result.State == "unsealed" && result.Failed == 0 && (result.Canary || result.Secrets == 0));
            case ["import", "--from-vault", var vault]:
                return Print(await sender.SendAsync(new VaultImportCommand(vault)), result => result.Failed == 0);
            case ["import", "--from-db", .. var rest] when rest is [] or ["--source-key-file", _]:
            {
                var connection = (await Console.In.ReadLineAsync())?.Trim();
                if (string.IsNullOrEmpty(connection))
                    return Usage("Give the standalone vault's connection string on stdin.");
                byte[]? key = null;
                if (rest is [_, var file])
                {
                    var text = File.Exists(file) ? (await File.ReadAllTextAsync(file)).Trim() : "";
                    key = new byte[48];
                    if (!Convert.TryFromBase64String(text, key, out var written) || written != 32)
                        return Usage("The source key file holds no base64 256-bit key.");
                    key = key[..32];
                }
                return Print(await sender.SendAsync(new VaultDatabaseImportCommand(connection, key)), result => result.Failed == 0);
            }
            default:
                return Usage("Usage: vault status | vault verify | vault import --from-vault <slug> | "
                             + "vault import --from-db [--source-key-file <path>]  (connection string on stdin)");
        }
    }

    private static int Print<T>(AppResult<T> result, Func<T, bool> passed) where T : notnull
    {
        if (result is AppResult<T>.Success { Data: var data })
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(data, JsonSerializerOptions.Web));
            return passed(data) ? 0 : 1;
        }
        Console.Error.WriteLine(((AppResult<T>.Failure)result).Error.Message);
        return 1;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
