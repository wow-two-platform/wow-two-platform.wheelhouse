using AwesomeAssertions;
using Wheelhouse.Application.Deployments;
using Wheelhouse.Application.Deployments.SelfDeploy.Interceptors;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>The guard on deploys of Wheelhouse's own targets: dev goes as any product's (never in a fleet); prod only by
/// the owner, with its ID typed and a reason, and never while a fleet runs.</summary>
public sealed class SelfDeploymentInterceptorTests
{
    private static DeploymentStartCommand Prod(string? confirm = "wheelhouse-prod", string? reason = "v0.5 trial",
        DeploymentCause? cause = null, Guid? fleet = null) =>
        new("wheelhouse-prod", "wheelhouse-v0-5-21", "max", confirm, Cause: cause, Reason: reason, Fleet: fleet);

    [Fact]
    public void TheOwnersTypedAndReasonedPromotion_ShouldGo() =>
        SelfDeploymentInterceptor.Refusal(Prod(), DeploymentEnvironment.Prod, fleetRunning: false).Should().BeNull();

    [Theory]
    [InlineData(null, "v0.5 trial", null, false, "Type wheelhouse-prod to promote Wheelhouse prod.")]
    [InlineData("wheelhouse-dev", "v0.5 trial", null, false, "Type wheelhouse-prod to promote Wheelhouse prod.")]
    [InlineData("wheelhouse-prod", " ", null, false, "Give a reason to promote Wheelhouse prod.")]
    [InlineData("wheelhouse-prod", "auto", DeploymentCause.AutoPromote, false, "Wheelhouse prod is promoted only by the owner; the automatic deploy was skipped.")]
    [InlineData("wheelhouse-prod", "v0.5 trial", null, true, "A fleet rollout is running; promote Wheelhouse prod after it ends, since the console restarts.")]
    public void Prod_ShouldBeRefused_UnlessTheOwnerPromotesItAlone(
        string? confirm, string? reason, DeploymentCause? cause, bool fleetRunning, string refusal) =>
        SelfDeploymentInterceptor.Refusal(Prod(confirm, reason, cause), DeploymentEnvironment.Prod, fleetRunning)
            .Should().Be(refusal);

    [Fact]
    public void Dev_ShouldTakeAutomaticAndUntypedDeploys_ButNeverAFleet()
    {
        var automatic = new DeploymentStartCommand("wheelhouse-dev", "wheelhouse-v0-5-21", "automation", Cause: DeploymentCause.AutoDev);
        SelfDeploymentInterceptor.Refusal(automatic, DeploymentEnvironment.Dev, fleetRunning: true).Should().BeNull();
        SelfDeploymentInterceptor.Refusal(automatic with { Fleet = Guid.NewGuid() }, DeploymentEnvironment.Dev, false)
            .Should().Be("Wheelhouse's own targets are never part of a fleet; promote it on its own.");
        SelfDeploymentInterceptor.Refusal(Prod(fleet: Guid.NewGuid()), DeploymentEnvironment.Prod, false)
            .Should().StartWith("Wheelhouse's own targets are never part of a fleet");
    }
}
