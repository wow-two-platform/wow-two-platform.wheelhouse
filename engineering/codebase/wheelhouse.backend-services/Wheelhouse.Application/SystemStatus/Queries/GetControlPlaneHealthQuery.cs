using Wheelhouse.Application.SystemStatus.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SystemStatus.Queries;

/// <summary>Checks the control plane's database readiness.</summary>
public sealed record GetControlPlaneHealthQuery(string Version) : IQuery<AppResult<ControlPlaneHealth>>;
