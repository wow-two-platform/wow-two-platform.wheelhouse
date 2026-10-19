using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments;
using Wheelhouse.Application.Deployments.SelfDeploy.Interceptors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Api.Configurations;

/// <summary>Wires Wheelhouse deploying itself (decision 64): the guard on deploys of its own targets, this console's
/// identity, and the read of its own newest rollout at boot.</summary>
public static class SelfDeployConfigurationExtensions
{
    /// <summary>Registers the guard after the application layer's interceptors, so it runs inside validation and the
    /// audit, then the console identity and the boot reader.</summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same builder.</returns>
    public static WebApplicationBuilder AddSelfDeploy(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IRequestInterceptor<DeploymentStartCommand, AppResult<JsonElement>>, SelfDeploymentInterceptor>();
        builder.Services.AddSingleton<IConsoleIdentity, Wheelhouse.Infrastructure.Deployments.ConsoleIdentity>();
        builder.Services.AddHostedService<Wheelhouse.Infrastructure.Deployments.SelfDeploymentBootReader>();
        return builder;
    }
}
