using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Mediator;

namespace Wheelhouse.Application.Inventory.Interceptors;

/// <summary>Exports the runner's inventory snapshot after every <see cref="IInventoryCommand"/> that succeeds, so the
/// runner and the runner CLI act on what the database now holds.</summary>
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

        // A refused change leaves the snapshot as it was.
        if (response is not IResult { IsSuccess: false })
            await services.GetRequiredService<IInventorySnapshot>().ExportAsync(CancellationToken.None);
        return response;
    }
}
