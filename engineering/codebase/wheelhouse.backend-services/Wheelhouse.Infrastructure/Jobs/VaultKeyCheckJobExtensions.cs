using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Jobs.Core;

namespace Wheelhouse.Infrastructure.Jobs;

/// <summary>Registers the scheduled check of the built-in vault's root key file as an SDK periodic job: at start, then
/// every <see cref="MinutesKey"/> minutes, the file must be present, readable by the console alone and open the canary.
/// A failure is an issue until the next pass passes.</summary>
public static class VaultKeyCheckJobExtensions
{
    /// <summary>Holds the job's name.</summary>
    public const string JobName = "vault-key-check";

    /// <summary>Holds the minutes between checks (default 60, bounded 1–1440; zero or less turns the check off).</summary>
    public const string MinutesKey = "Vault:KeyCheckMinutes";

    /// <summary>Adds the job.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddVaultKeyCheckJob(this IServiceCollection services) =>
        services.AddPeriodicJob<RootKeyChecks>(
            JobName,
            (checks, ct) => checks.CheckFileAsync(ct),
            (options, root) =>
            {
                var minutes = root.GetRequiredService<IConfiguration>().GetValue(MinutesKey, 60);
                options.Interval = minutes <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440));
                options.Cadence = PeriodicJobCadence.FixedDelay;
                options.LogExceptionDetails = false;
            });
}
