using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Inventory.Interceptors;

/// <summary>Exports the runner's inventory snapshot after every <see cref="IInventoryCommand"/> that succeeds, so the
/// runner and the operator CLI act on what the database now holds.</summary>
public sealed class InventoryExportingInterceptor<TRequest, TResponse>(IServiceProvider services)
    : IRequestInterceptor<TRequest, TResponse> where TRequest : notnull
{
    /// <inheritdoc />
    public async ValueTask<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> nextStep, CancellationToken cancellationToken)
    {
        var response = await nextStep();
        if (request is not IInventoryCommand)
            return response;

        // Only AppResult<T>.Failure carries an Error; a refused change leaves the snapshot as it was.
        var error = response?.GetType().GetProperty(nameof(AppResult<object>.Failure.Error))?.GetValue(response) as AppError;
        if (error is null)
            await services.GetRequiredService<IInventorySnapshot>().ExportAsync(CancellationToken.None);
        return response;
    }
}
