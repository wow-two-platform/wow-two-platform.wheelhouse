using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Queries;

/// <summary>Reads the settings a release takes on a target and where the next deploy's render takes each from.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Release">The release bundle's identifier.</param>
public sealed record DeploymentSettingsQuery(string Target, string Release) : IQuery<AppResult<DeploySettingsDto>>;
