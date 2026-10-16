using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

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
