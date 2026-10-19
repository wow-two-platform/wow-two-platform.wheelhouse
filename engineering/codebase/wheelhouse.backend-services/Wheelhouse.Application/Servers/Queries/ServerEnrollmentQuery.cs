using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.Queries;

/// <summary>Reads a server's enrollment with its live steps.</summary>
/// <param name="Slug">The server's slug.</param>
public sealed record ServerEnrollmentQuery(string Slug) : IQuery<AppResult<JsonElement>>;
