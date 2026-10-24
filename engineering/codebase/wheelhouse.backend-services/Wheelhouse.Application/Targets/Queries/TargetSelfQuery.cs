using Wheelhouse.Application.Targets.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Targets.Queries;

/// <summary>Represents a query for what Wheelhouse derives for its own environments.</summary>
public sealed record TargetSelfQuery : IQuery<AppResult<SelfTargetDto>>;
