using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Queries;

/// <summary>Lists a failed export of the runner's inventory snapshot, until an export succeeds again.</summary>
public sealed record DeploymentInventoryExportQuery : IQuery<AppResult<IReadOnlyList<InventoryExportIssueDto>>>;
