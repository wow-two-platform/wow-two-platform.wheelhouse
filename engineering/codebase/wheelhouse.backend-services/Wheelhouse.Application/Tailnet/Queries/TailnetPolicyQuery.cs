using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Tailnet.Queries;

/// <summary>Reads the live tailnet policy beside the one Wheelhouse renders, for the owner's review.</summary>
public sealed record TailnetPolicyQuery : IQuery<AppResult<JsonElement>>;
