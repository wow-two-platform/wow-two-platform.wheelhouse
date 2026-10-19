using Microsoft.Extensions.Configuration;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Infrastructure.Infra;

/// <summary>Refuses infrastructure on deployed dev: the runner names every deployed console's environment in
/// <c>DEPLOY_ENVIRONMENT</c>, and dev never holds a products host's keys or the Tailscale client. Prod and a run on the
/// developer's machine may.</summary>
public sealed class ConsoleInfraAccess(IConfiguration configuration) : IInfraAccess
{
    /// <summary>The variable the runner sets on every deployed container: the environment it runs in.</summary>
    public const string DeployEnvironmentKey = "DEPLOY_ENVIRONMENT";

    /// <summary>The refusal deployed dev answers with.</summary>
    public const string DevRefusal =
        "Only Wheelhouse prod drives Tailscale, enrolls servers and places products; dev never holds their keys.";

    /// <inheritdoc />
    public string? Refusal =>
        string.Equals(configuration[DeployEnvironmentKey], "dev", StringComparison.OrdinalIgnoreCase) ? DevRefusal : null;
}
