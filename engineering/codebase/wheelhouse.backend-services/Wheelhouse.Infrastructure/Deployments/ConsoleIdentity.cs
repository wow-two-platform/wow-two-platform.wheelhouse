using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>This console as the runner deployed it: <c>DEPLOY_ENVIRONMENT</c>, which the runner gives every deployed
/// service, else <c>prod</c> when it runs as Production and <c>dev</c> otherwise (as <c>/api/system/status</c> reports
/// it); its start is when the host built this singleton.</summary>
public sealed class ConsoleIdentity(IConfiguration configuration, IHostEnvironment host, TimeProvider time) : IConsoleIdentity
{
    /// <inheritdoc />
    public string Environment { get; } = configuration["DEPLOY_ENVIRONMENT"]?.Trim().ToLowerInvariant() switch
    {
        "dev" => "dev",
        "prod" => "prod",
        _ => host.IsProduction() ? "prod" : "dev",
    };

    /// <inheritdoc />
    public DateTimeOffset BootedAt { get; } = time.GetUtcNow();
}
