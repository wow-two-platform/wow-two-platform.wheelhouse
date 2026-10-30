using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.SelfDeploy.Constants;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>This console as the runner deployed it: <c>DEPLOY_ENVIRONMENT</c>, which the runner gives every deployed
/// service, else <c>prod</c> when it runs as Production and <c>dev</c> otherwise (as <c>/api/system/status</c> reports
/// it), read on each use; its start is when the host built this singleton; its hostname is read from its settings on each use.</summary>
public sealed class ConsoleIdentity(IConfiguration configuration, IHostEnvironment host, TimeProvider time) : IConsoleIdentity
{
    /// <summary>The key a Wheelhouse deploy renders the console site's URL under.</summary>
    public const string SiteUrlKey = "Sites:" + SelfProduct.Site + ":Url";

    /// <inheritdoc />
    public string Environment => configuration.GetDeployEnvironment() switch
    {
        "dev" => "dev",
        "prod" => "prod",
        _ => host.IsProduction() ? "prod" : "dev",
    };

    /// <inheritdoc />
    public DateTimeOffset BootedAt { get; } = time.GetUtcNow();

    /// <inheritdoc />
    public string? Host => HostOf(configuration);

    /// <summary>Reads the hostname a console's settings name: the console site's URL, else the first entry of
    /// <c>AllowedHosts</c> that is a host name of two labels or more — not <c>*</c>, a wildcard, <c>localhost</c>, the
    /// console's container name or an IP address.</summary>
    /// <param name="configuration">The console's configuration.</param>
    /// <returns>The hostname, lowercase, or <c>null</c> when the settings name none.</returns>
    public static string? HostOf(IConfiguration configuration)
    {
        if (Uri.TryCreate(configuration[SiteUrlKey]?.Trim(), UriKind.Absolute, out var site) && IsName(site.Host))
            return site.Host.ToLowerInvariant();
        return (configuration["AllowedHosts"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.ToLowerInvariant())
            .FirstOrDefault(IsName);
    }

    private static bool IsName(string host) =>
        Regex.IsMatch(host.ToLowerInvariant(), FormatPatternConstants.DomainName) && !IPAddress.TryParse(host, out _);
}
