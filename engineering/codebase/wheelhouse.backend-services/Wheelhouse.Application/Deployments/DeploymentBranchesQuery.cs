using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Lists the branches of one of a product's repositories: <paramref name="Repository"/>, else its primary one.</summary>
public sealed record DeploymentBranchesQuery(string Product, string? Repository = null) : IQuery<AppResult<JsonElement>>;
