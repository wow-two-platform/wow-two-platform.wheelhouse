namespace Wheelhouse.Application.Deployments.Fleets.Services;

/// <summary>Lets one caller at a time move fleets on — the request that starts or cancels one, or the background pass —
/// so a product is never submitted twice. Held for the process; Wheelhouse runs as one instance.</summary>
public sealed class FleetRolloutGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the gate.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A handle that releases the gate when disposed.</returns>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Release(_gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}
