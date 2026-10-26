using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Sessions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Mediator;

namespace Wheelhouse.Application.Inventory.Interceptors;

/// <summary>Exports the runner's inventory snapshot once every <see cref="IInventoryCommand"/> that succeeds is
/// committed, so the runner and the runner CLI act on what the database now holds.</summary>
/// <remarks>The export is the SDK data session's after-commit hook: it runs at once when the command wrote outside a
/// transaction, after the commit when it ran inside one, once per transaction, and never after a rollback. A failed
/// export is logged and leaves the change saved; the next change or start repairs the snapshot.</remarks>
public sealed class InventoryExportingInterceptor<TRequest, TResponse>(IServiceProvider services)
    : IRequestInterceptor<TRequest, TResponse> where TRequest : notnull
{
    /// <summary>Holds the key that keeps one export per transaction however many inventory commands it holds.</summary>
    private const string ExportKey = "wheelhouse:inventory-export";

    /// <inheritdoc />
    public async ValueTask<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> nextStep, CancellationToken cancellationToken)
    {
        var response = await nextStep();
        // A refused change leaves the snapshot as it was.
        if (request is not IInventoryCommand || response is IResult { IsSuccess: false })
            return response;

        var snapshot = services.GetRequiredService<IInventorySnapshot>();
        await services.GetRequiredService<IDataSession>()
            .OnCommittedAsync(token => new ValueTask(snapshot.ExportAsync(token)), ExportKey);
        return response;
    }
}
