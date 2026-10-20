using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.Queries;

/// <summary>Reads a server's upkeep runs with their live steps, and the digest of the kit this console ships.</summary>
/// <param name="Slug">The server's slug.</param>
public sealed record ServerUpkeepsQuery(string Slug) : IQuery<AppResult<JsonElement>>;
