using Microsoft.Extensions.Configuration;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace Wheelhouse.Infrastructure.Infra;

/// <summary>Refuses infrastructure on deployed dev: the runner names every deployed console's environment in
/// <c>DEPLOY_ENVIRONMENT</c>, and dev never holds a products host's keys or the Tailscale client. Prod and a run on the
/// developer's machine may.</summary>
public sealed class ConsoleInfraAccess(IConfiguration configuration) : IInfraAccess
{
    /// <summary>The refusal deployed dev answers with.</summary>
    public const string DevRefusal =
        "Only Wheelhouse prod drives Tailscale, enrolls servers, places products and refreshes host kits; "
        + "dev never holds their keys.";

    /// <inheritdoc />
    public string? Refusal =>
        configuration.GetDeployEnvironment() == "dev" ? DevRefusal : null;
}
