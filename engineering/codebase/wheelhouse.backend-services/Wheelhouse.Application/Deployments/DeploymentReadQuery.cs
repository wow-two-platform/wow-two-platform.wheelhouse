using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Reads a deployment resource from the runner.</summary>
public sealed record DeploymentReadQuery(string Resource, string? Id = null) : IQuery<AppResult<JsonElement>>;
