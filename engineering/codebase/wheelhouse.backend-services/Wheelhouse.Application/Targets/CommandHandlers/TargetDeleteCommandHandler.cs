using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Commands;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.CommandHandlers;

/// <summary>Handles <see cref="TargetDeleteCommand"/>.</summary>
public sealed class TargetDeleteCommandHandler(ITargetsRepository targets)
    : ICommandHandler<TargetDeleteCommand, AppResult<Unit>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<Unit>> HandleAsync(TargetDeleteCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Slug, cancellationToken);
        if (target is null)
            return AppResult<Unit>.Fail(AppErrorFactory.NotFound($"Target '{request.Slug}' was not found."));

        await targets.DeleteAsync(target, cancellationToken);
        return AppResult<Unit>.Ok(default);
    }
}
