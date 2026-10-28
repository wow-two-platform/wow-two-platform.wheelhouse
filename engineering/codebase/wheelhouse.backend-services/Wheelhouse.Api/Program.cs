using Wheelhouse.Api.Cli;
using Wheelhouse.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Data;

// `migrate …` answers the be sdk's migration verbs (the rollout's migrate step runs the bare `migrate`) and `vault …`
// runs a Secrets Vault command (Cli/VaultCli.cs), against this host's database, then exits. Neither reads its
// arguments as configuration.
var migrate = MigrateCommand.IsCommand(args);
var vault = VaultCli.IsCommand(args);
var builder = WebApplication.CreateBuilder(migrate || vault ? [] : args);
builder.Configure();

var app = builder.Build();
if (migrate)
    return await app.RunMigrateCommandAsync(args);
if (vault)
    return await VaultCli.RunAsync(app, args);
app.Configure();

// A local run creates the database and applies pending migrations; a deployed boot only checks that the database
// suits this build, since the rollout's migrate step applied them before it started.
await app.Services.MigrateBespokeOnStartupAsync();

app.Run();
return 0;
