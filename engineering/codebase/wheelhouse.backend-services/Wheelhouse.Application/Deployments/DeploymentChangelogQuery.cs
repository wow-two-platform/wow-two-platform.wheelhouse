using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Reads what changed between two releases of a product: the release bodies published after
/// <paramref name="From"/> up to <paramref name="To"/>, and the compare commit list; <paramref name="From"/> is what the
/// target runs now, or null when it runs nothing.</summary>
public sealed record DeploymentChangelogQuery(string Product, string? From, string To) : IQuery<AppResult<JsonElement>>;
