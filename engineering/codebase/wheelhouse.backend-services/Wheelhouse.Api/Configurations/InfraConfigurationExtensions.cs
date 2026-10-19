using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.InfraJobs;

namespace Wheelhouse.Api.Configurations;

/// <summary>Wires infrastructure automation: Tailscale, enrollment and placement through the runner's <c>infra.py</c>,
/// the follower that records finished jobs, and the rule that only prod (or a local run) drives any of it.</summary>
public static class InfraConfigurationExtensions
{
    /// <summary>Registers the infrastructure gateway, its access rule, the job recorder and its follower.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddInfraAutomation(this IServiceCollection services)
    {
        services.AddScoped<IInfraGateway, Wheelhouse.Infrastructure.Infra.InfraGateway>();
        services.AddSingleton<IInfraAccess, Wheelhouse.Infrastructure.Infra.ConsoleInfraAccess>();
        services.AddScoped<InfraJobRecorder>();
        services.AddHostedService<Wheelhouse.Infrastructure.Infra.InfraJobFollower>();
        return services;
    }
}
