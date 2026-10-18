namespace Wheelhouse.Application.Deployments.Automation.Models;

/// <summary>Represents what one automation pass did.</summary>
/// <param name="Queued">How many deploys it queued or replaced.</param>
/// <param name="Submitted">How many queued deploys it submitted.</param>
/// <param name="Dropped">How many queued deploys it dropped: refused, frozen, turned off or already done.</param>
public sealed record DeploymentAutomationPass(int Queued, int Submitted, int Dropped);
