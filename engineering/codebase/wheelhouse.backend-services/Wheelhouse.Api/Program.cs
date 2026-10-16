using Wheelhouse.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Configure();

var app = builder.Build();
app.Configure();

// Create the database if missing, then apply all pending bespoke SQL migrations (idempotent — runs on every boot).
await app.Services.MigrateBespokeOnStartupAsync();

app.Run();
