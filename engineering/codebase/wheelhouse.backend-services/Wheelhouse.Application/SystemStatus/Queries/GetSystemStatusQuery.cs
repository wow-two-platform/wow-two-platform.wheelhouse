using Wheelhouse.Application.SystemStatus.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SystemStatus.Queries;

/// <summary>Reads the running console's version and deployment mode.</summary>
/// <param name="Version">The version of the executable serving the request.</param>
public sealed record GetSystemStatusQuery(string Version) : IQuery<AppResult<SystemStatusModel>>;
