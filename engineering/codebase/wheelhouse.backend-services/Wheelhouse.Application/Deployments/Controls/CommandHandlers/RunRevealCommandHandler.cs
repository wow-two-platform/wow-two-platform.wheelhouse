using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Targets.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Controls.CommandHandlers;

/// <summary>Handles <see cref="RunRevealCommand"/>: an unknown target is not found; the runner refuses a job that is no
/// run of that target, and a line read once already or past its time.</summary>
public sealed class RunRevealCommandHandler(IDeploymentGateway gateway, ITargetsRepository targets)
    : ICommandHandler<RunRevealCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(RunRevealCommand request, CancellationToken cancellationToken)
    {
        if (await targets.GetBySlugAsync(request.Target, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        return await gateway.RevealRunAsync(request.Target, request.Job.ToString("D"), cancellationToken);
    }
}
