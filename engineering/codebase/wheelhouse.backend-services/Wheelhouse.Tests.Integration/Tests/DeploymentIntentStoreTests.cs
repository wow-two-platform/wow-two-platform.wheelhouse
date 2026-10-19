using AwesomeAssertions;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The deploys that never reached their target, over a real relational database: each outcome, refusal and cause
/// round-trips as snake_case text, and they read back oldest first.
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class DeploymentIntentStoreTests(WheelhouseTestDb db) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-18T23:40:00Z");

    /// <inheritdoc />
    public async Task InitializeAsync() => await db.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Intents_ShouldRoundTripTheirOutcomeRefusalAndCause_OldestFirst()
    {
        await using (var ctx = db.NewContext())
        {
            var store = new DeploymentAutomationStore(ctx);
            await store.AddIntentAsync(Intent(At.AddMinutes(5), DeploymentIntentOutcome.Superseded, null, DeploymentCause.AutoDev, "v1.8.0"));
            await store.AddIntentAsync(Intent(At, DeploymentIntentOutcome.Refused, DeploymentRefusal.Freeze, null, null));
        }

        await using var read = db.NewContext();
        var intents = await new DeploymentAutomationStore(read).GetIntentsAsync();
        intents.Select(row => (row.Outcome, row.Refusal, row.Cause, row.ReplacedBy)).Should().Equal(
            (DeploymentIntentOutcome.Refused, DeploymentRefusal.Freeze, (DeploymentCause?)null, (string?)null),
            (DeploymentIntentOutcome.Superseded, (DeploymentRefusal?)null, DeploymentCause.AutoDev, "v1.8.0"));
        intents[0].At.Should().Be(At);
        (intents[1].Target, intents[1].Product, intents[1].Environment).Should().Be(("pilot-dev", "pilot", "dev"));
    }

    private static DeploymentIntentEntity Intent(
        DateTimeOffset at, DeploymentIntentOutcome outcome, DeploymentRefusal? refusal, DeploymentCause? cause, string? replacedBy) => new()
    {
        Id = Guid.NewGuid(), Target = "pilot-dev", Product = "pilot", Environment = "dev", BundleId = "pilot-gh-7", Release = "v1.7.3",
        Actor = "automation", Outcome = outcome, Refusal = refusal, Cause = cause, ReplacedBy = replacedBy, At = at,
    };
}
