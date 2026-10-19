using Wheelhouse.Api.Cli;
using Wheelhouse.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Data;

// `vault …` runs a Secrets Vault command against this host's database and root key, then exits (Cli/VaultCli.cs).
var command = VaultCli.IsCommand(args);
var builder = WebApplication.CreateBuilder(command ? [] : args);
builder.Configure();

var app = builder.Build();
if (command)
    return await VaultCli.RunAsync(app, args);
app.Configure();

// Create the database if missing, then apply all pending bespoke SQL migrations (idempotent — runs on every boot).
await app.Services.MigrateBespokeOnStartupAsync();

app.Run();
return 0;
