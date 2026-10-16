using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Reads a branch's recent commits, each with its build when one exists.</summary>
public sealed record DeploymentCommitsQuery(string Product, string Branch) : IQuery<AppResult<JsonElement>>;
