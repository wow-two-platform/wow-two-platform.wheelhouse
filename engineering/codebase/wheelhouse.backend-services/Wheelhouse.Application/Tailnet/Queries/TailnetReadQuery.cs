using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.Queries;

/// <summary>Reads whether Tailscale is connected and each server's tailnet node.</summary>
public sealed record TailnetReadQuery : IQuery<AppResult<JsonElement>>;
