using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.FilesMigrations.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.FilesMigrations.CommandHandlers;

/// <summary>Handles <see cref="FilesMigrationCancelCommand"/>: the job must be a files migration of that target; the
/// runner stops it and its journal keeps the outcome.</summary>
public sealed class FilesMigrationCancelCommandHandler(IDeploymentGateway gateway, ITargetsRepository targets, IDeploymentRecords records)
    : ICommandHandler<FilesMigrationCancelCommand, AppResult<JsonElement>>
{
    /// <summary>Holds the operation kind of a files migration job.</summary>
    public const string Kind = "files-migration";

    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(FilesMigrationCancelCommand request, CancellationToken cancellationToken)
    {
        if (await targets.GetBySlugAsync(request.Target, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        if (await records.ListAsync(cancellationToken) is not AppResult<IReadOnlyList<Deployments.History.Models.DeploymentRecord>>.Success { Data: var all })
            return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable("Deployment history could not be read."));
        var job = request.Job.ToString("D");
        var record = all.FirstOrDefault(item => string.Equals(item.Id, job, StringComparison.OrdinalIgnoreCase) && item.TargetId == request.Target);
        if (record?.Operation?.Kind != Kind)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict($"Job {job} is no files migration of {request.Target}."));
        return await gateway.CancelControlAsync(request.Target, job, request.Actor, cancellationToken);
    }
}
