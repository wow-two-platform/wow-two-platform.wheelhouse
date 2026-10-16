using Wheelhouse.Application.Integrations.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Integrations.Queries;

/// <summary>Represents a query to get every integration key, revoked ones included.</summary>
public sealed record IntegrationKeyGetAllQuery : IQuery<AppResult<IReadOnlyList<IntegrationKeyDto>>>;
