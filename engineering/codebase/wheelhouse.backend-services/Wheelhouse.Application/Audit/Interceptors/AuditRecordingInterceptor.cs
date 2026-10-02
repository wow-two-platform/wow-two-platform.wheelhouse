using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Audit.Enums;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Audit.Interceptors;

/// <summary>Records the outcome of every <see cref="IAuditedCommand"/> in the audit trail, refusals included. It wraps
/// validation, so a rejected request is recorded too; other requests pass through untouched.</summary>
public sealed class AuditRecordingInterceptor<TRequest, TResponse>(
    IServiceProvider services, ILogger<AuditRecordingInterceptor<TRequest, TResponse>> logger)
    : IRequestInterceptor<TRequest, TResponse> where TRequest : notnull
{
    private const int ReasonLength = 300;

    /// <inheritdoc />
    public async ValueTask<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> nextStep, CancellationToken cancellationToken)
    {
        if (request is not IAuditedCommand audited)
            return await nextStep();

        TResponse response;
        try
        {
            response = await nextStep();
        }
        catch (Exception)
        {
            await RecordAsync(audited, AuditOutcome.Failed, "The action failed unexpectedly.");
            throw;
        }

        // Only AppResult<T>.Failure carries an Error; its message is the operator-safe text the controller renders.
        var error = response?.GetType().GetProperty(nameof(AppResult<object>.Failure.Error))?.GetValue(response) as AppError;
        if (error is null)
            await RecordAsync(audited, AuditOutcome.Succeeded, null);
        else
            await RecordAsync(audited, AuditOutcome.Failed,
                error.Message is { Length: > 0 } message ? message[..Math.Min(message.Length, ReasonLength)] : null);
        return response;
    }

    private async Task RecordAsync(IAuditedCommand audited, AuditOutcome outcome, string? reason)
    {
        // Resolved only for audited commands, so a read never depends on the audit store.
        var record = new AuditRecord(services.GetRequiredService<IOperatorContext>().Actor, audited.AuditAction,
            audited.AuditSubject, outcome, audited.AuditDetail, reason);
        try
        {
            // The action has already happened; a caller that disconnects must not cancel its record.
            await services.GetRequiredService<IAuditTrail>().AppendAsync(record, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Audit trail append failed for {Action} on {Subject} by {Actor} ({Outcome})",
                record.Action, record.Subject, record.Actor, record.Outcome);
        }
    }
}
