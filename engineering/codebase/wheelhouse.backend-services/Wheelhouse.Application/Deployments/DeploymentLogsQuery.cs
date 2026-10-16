using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Reads the last lines one service's container wrote on a target; nothing stores them.</summary>
/// <param name="Target">The target the service runs on.</param>
/// <param name="Service">The service whose container output to read.</param>
/// <param name="Tail">How many lines, 1-1000.</param>
public sealed record DeploymentLogsQuery(string Target, string Service, int Tail) : IQuery<AppResult<JsonElement>>;
