using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Configurations;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// The host's local-run rules: deployed dev runs as Development, so only a local run — Development without the runner's
/// <c>DEPLOY_ENVIRONMENT</c> — reads this machine's <c>appsettings.Local.json</c> (and only when its launch profile
/// asks) or boots with an open allowlist; a deployed host reads the <c>appsettings.Target.json</c> its bundle mounts.
/// Builds bare hosts over a throwaway content root; no database.
/// </summary>
public sealed class LocalRunE2ETests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("wheelhouse-local-run-");

    public LocalRunE2ETests()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "appsettings.json"), """{ "Probe": { "Shared": "base", "Machine": "base" } }""");
        File.WriteAllText(Path.Combine(_root.FullName, "appsettings.Development.json"), """{ "Probe": { "Machine": "development" } }""");
        File.WriteAllText(Path.Combine(_root.FullName, SettingsFileExtensions.LocalFile),
            """{ "Probe": { "Machine": "machine", "Override": "machine" } }""");
    }

    [Fact]
    public void LocalSettings_ShouldApplyAfterTheEnvironmentFile_OnALocalRunWhoseProfileAsks()
    {
        var builder = Builder(new() { ["LOCAL_SETTINGS"] = "true", ["Probe:Override"] = "explicit" });
        builder.AddSettings();

        builder.Configuration["Probe:Machine"].Should().Be("machine", "the machine file follows appsettings.Development.json");
        builder.Configuration["Probe:Override"].Should().Be("explicit", "every later source still wins over it");
        builder.Configuration["Probe:Shared"].Should().Be("base");
    }

    [Theory]
    [InlineData(null, "Development")]   // a test host or a bare run: the launch profile never asked
    [InlineData("dev", "Development")]  // deployed dev runs as Development; the runner names it
    [InlineData(null, "Production")]
    public void LocalSettings_ShouldStayUnread_OutsideALocalRunThatAsks(string? deployed, string environment)
    {
        File.WriteAllText(Path.Combine(_root.FullName, SettingsFileExtensions.TargetFile), """{ "Probe": { "Target": "target" } }""");
        var settings = new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployed };
        if (deployed is not null || environment == "Production")
            settings["LOCAL_SETTINGS"] = "true";
        var builder = Builder(settings, environment);
        builder.AddSettings();

        builder.Configuration["Probe:Machine"].Should().Be(environment == "Development" ? "development" : "base");
        builder.Configuration["Probe:Override"].Should().BeNull();
        builder.Configuration["Probe:Target"].Should().Be("target", "a target's mounted file applies on every run");
    }

    [Fact]
    public void TargetSettings_ShouldApply_OnADeployedConsole()
    {
        File.WriteAllText(Path.Combine(_root.FullName, SettingsFileExtensions.TargetFile),
            """{ "Probe": { "Machine": "target" }, "Deployment": { "TrustedProxies": [ "10.0.0.2" ] } }""");
        var builder = Builder(new() { ["DEPLOY_ENVIRONMENT"] = "dev" });
        builder.AddSettings();

        builder.Configuration["Probe:Machine"].Should().Be("target");
        builder.Configuration["Deployment:TrustedProxies:0"].Should().Be("10.0.0.2", "AddPlatformDefaults reads it next");
    }

    [Theory]
    [InlineData("dev", "Development")]
    [InlineData("prod", "Production")]
    [InlineData(null, "Production")]
    public void Allowlist_ShouldBeRequired_WhereverTheConsoleIsDeployed(string? deployed, string environment)
    {
        var builder = Builder(new() { ["DEPLOY_ENVIRONMENT"] = deployed }, environment);

        // Registering needs no allowlist, so a migrate or vault command builds the host without the console's settings.
        builder.AddAuthentication();
        var app = builder.Build();
        var serve = () => app.RequireLoginAllowlist();

        serve.Should().Throw<InvalidOperationException>().WithMessage("A deployed console requires Identity:AllowedGitHubLogins.");
    }

    [Fact]
    public void Allowlist_MayStayEmpty_OnALocalRun()
    {
        var builder = Builder([]);
        builder.AddAuthentication();
        var app = builder.Build();

        var serve = () => app.RequireLoginAllowlist();

        serve.Should().NotThrow();
        builder.Environment.IsLocalRun(builder.Configuration).Should().BeTrue();
    }

    /// <summary>A bare host over the throwaway content root, without the process environment, so the E2E fixture's
    /// allowlist variable and a developer's shell stay out; <paramref name="settings"/> stand in for variables.</summary>
    private WebApplicationBuilder Builder(Dictionary<string, string?> settings, string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = _root.FullName,
            EnvironmentName = environment,
        });
        foreach (var source in builder.Configuration.Sources.OfType<EnvironmentVariablesConfigurationSource>().ToArray())
            builder.Configuration.Sources.Remove(source);
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    /// <inheritdoc />
    public void Dispose() => _root.Delete(recursive: true);
}
