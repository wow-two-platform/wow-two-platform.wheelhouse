using Microsoft.Extensions.Logging.Abstractions;
using Wheelhouse.Infrastructure.Deployments;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Creates the SDK process client the way the host configures the runner's: its time limit, environment
/// allowlist and failure parser.</summary>
public static class RunnerProcessClients
{
    /// <summary>Creates a runner process client.</summary>
    /// <returns>The client.</returns>
    public static IProcessClient Create()
    {
        var options = new ProcessClientOptions();
        RunnerProcess.Configure(options);
        return new ProcessClient(options, TimeProvider.System, NullLogger<ProcessClient>.Instance);
    }
}
