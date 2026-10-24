using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Application.Targets.Queries;
using Wheelhouse.Application.Targets.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Targets.QueryHandlers;

/// <summary>Handles <see cref="TargetSelfQuery"/> from this console's own hostname (<see cref="SelfTargets"/>).</summary>
public sealed class TargetSelfQueryHandler(IConsoleIdentity console) : IQueryHandler<TargetSelfQuery, AppResult<SelfTargetDto>>
{
    /// <inheritdoc />
    public ValueTask<AppResult<SelfTargetDto>> HandleAsync(TargetSelfQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AppResult<SelfTargetDto>.Ok(SelfTargets.Derive(console.Host)));
}
