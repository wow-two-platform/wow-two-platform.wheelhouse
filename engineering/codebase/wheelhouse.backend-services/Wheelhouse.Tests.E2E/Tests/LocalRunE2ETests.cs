using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Configurations;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// The host's local-run rules: deployed dev runs as Development, so only a local run — Development without the runner's
/// <c>DEPLOY_ENVIRONMENT</c> — reads <c>appsettings.Machine.json</c> (and only when its launch profile asks) or boots with
/// an open allowlist. Builds bare hosts over a throwaway content root; no database.
/// </summary>
public sealed class LocalRunE2ETests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("wheelhouse-local-run-");

    public LocalRunE2ETests()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "appsettings.json"), """{ "Probe": { "Shared": "base", "Machine": "base" } }""");
        File.WriteAllText(Path.Combine(_root.FullName, "appsettings.Development.json"), """{ "Probe": { "Machine": "development" } }""");
        File.WriteAllText(Path.Combine(_root.FullName, LocalRun.MachineSettingsFile),
            """{ "Probe": { "Machine": "machine", "Override": "machine" } }""");
    }

    [Fact]
    public void MachineSettings_ShouldApplyAfterTheEnvironmentFile_OnALocalRunWhoseProfileAsks()
    {
        var builder = Builder(new() { ["MACHINE_SETTINGS"] = "true", ["Probe:Override"] = "explicit" });
        builder.AddSettings();

        builder.Configuration["Probe:Machine"].Should().Be("machine", "the machine file follows appsettings.Development.json");
        builder.Configuration["Probe:Override"].Should().Be("explicit", "every later source still wins over it");
        builder.Configuration["Probe:Shared"].Should().Be("base");
    }

    [Theory]
    [InlineData(null, "Development")]   // a test host or a bare run: the launch profile never asked
    [InlineData("dev", "Development")]  // deployed dev runs as Development; the runner names it
    [InlineData(null, "Production")]
    public void MachineSettings_ShouldStayUnread_OutsideALocalRunThatAsks(string? deployed, string environment)
    {
        var settings = new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployed };
        if (deployed is not null || environment == "Production")
            settings["MACHINE_SETTINGS"] = "true";
        var builder = Builder(settings, environment);
        builder.AddSettings();

        builder.Configuration["Probe:Machine"].Should().Be(environment == "Development" ? "development" : "base");
        builder.Configuration["Probe:Override"].Should().BeNull();
    }

    [Theory]
    [InlineData("dev", "Development")]
    [InlineData("prod", "Production")]
    [InlineData(null, "Production")]
    public void Allowlist_ShouldBeRequired_WhereverTheConsoleIsDeployed(string? deployed, string environment)
    {
        var builder = Builder(new() { ["DEPLOY_ENVIRONMENT"] = deployed }, environment);

        var boot = () => builder.AddAuthentication();

        boot.Should().Throw<InvalidOperationException>().WithMessage("A deployed console requires Identity:AllowedGitHubLogins.");
    }

    [Fact]
    public void Allowlist_MayStayEmpty_OnALocalRun()
    {
        var builder = Builder([]);

        var boot = () => builder.AddAuthentication();

        boot.Should().NotThrow();
        LocalRun.IsLocal(builder.Environment, builder.Configuration).Should().BeTrue();
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
