namespace Wheelhouse.Api.Configurations;

/// <summary>Tells a run on the developer's machine from a deployed one. The runner gives every deployed container
/// <c>DEPLOY_ENVIRONMENT</c>, and deployed dev runs as <c>Development</c> too, so Development alone never means this
/// machine. The be sdk's <c>AddSettingsFiles</c> keys this machine's own settings file on the same rule.</summary>
public static class LocalRun
{
    /// <summary>Holds the variable the runner sets on every deployed container: the environment it runs in.</summary>
    public const string DeployEnvironmentKey = "DEPLOY_ENVIRONMENT";

    /// <summary>Whether the runner deployed this host: it names the environment the host runs in.</summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>True for a deployed host.</returns>
    public static bool IsDeployed(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[DeployEnvironmentKey]);

    /// <summary>Whether this is a run on the developer's machine: Development, and not deployed.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>True for a local run.</returns>
    public static bool IsLocal(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && !IsDeployed(configuration);
}
