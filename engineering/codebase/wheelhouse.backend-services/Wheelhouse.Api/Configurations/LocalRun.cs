using Microsoft.Extensions.Configuration.Json;

namespace Wheelhouse.Api.Configurations;

/// <summary>Tells a run on the developer's machine from a deployed one. The runner gives every deployed container
/// <c>DEPLOY_ENVIRONMENT</c>, and deployed dev runs as <c>Development</c> too, so Development alone never means this
/// machine.</summary>
public static class LocalRun
{
    /// <summary>Holds the variable the runner sets on every deployed container: the environment it runs in.</summary>
    public const string DeployEnvironmentKey = "DEPLOY_ENVIRONMENT";

    /// <summary>Holds the variable the IDE launch profile sets for a local run, so it reads <see cref="MachineSettingsFile"/>.</summary>
    public const string MachineSettingsKey = "MACHINE_SETTINGS";

    /// <summary>Holds this machine's settings: the local rig's database, runner paths and switch. Gitignored, outside
    /// the image, and read by a local run only.</summary>
    public const string MachineSettingsFile = "appsettings.Machine.json";

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

    /// <summary>Whether this run reads <see cref="MachineSettingsFile"/>: a local run whose launch profile sets
    /// <c>MACHINE_SETTINGS=true</c>. Tests and deployed containers never set it.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>True when the machine settings apply.</returns>
    public static bool ReadsMachineSettings(IHostEnvironment environment, IConfiguration configuration) =>
        IsLocal(environment, configuration) && bool.TryParse(configuration[MachineSettingsKey], out var on) && on;

    /// <summary>Adds <see cref="MachineSettingsFile"/> right after <c>appsettings.{Environment}.json</c>, so user
    /// secrets, environment variables and every later source still win over it; optional, since a machine without the
    /// local rig has none.</summary>
    /// <param name="configuration">The host configuration to extend.</param>
    /// <param name="environment">The host environment, which names the file it follows.</param>
    public static void AddMachineSettings(IConfigurationBuilder configuration, IHostEnvironment environment)
    {
        var environmentFile = "appsettings." + environment.EnvironmentName + ".json";
        var sources = configuration.Sources;
        var after = -1;
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index] is JsonConfigurationSource { Path: var path }
                && string.Equals(path, environmentFile, StringComparison.OrdinalIgnoreCase))
                after = index;
        }

        // Read from the content root, as the other settings files are.
        var machine = new JsonConfigurationSource
        {
            Path = MachineSettingsFile, Optional = true, ReloadOnChange = false, FileProvider = configuration.GetFileProvider(),
        };
        sources.Insert(after < 0 ? sources.Count : after + 1, machine);
    }
}
