using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.CommandHandlers;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Application.Tailnet.Queries;
using Wheelhouse.Application.Tailnet.QueryHandlers;
using Wheelhouse.Infrastructure.Infra;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Infra;

/// <summary>Tests that only prod (or a local run) drives infrastructure: deployed dev never asks the runner.</summary>
public sealed class InfraAccessTests
{
    private static ConsoleInfraAccess Access(string? environment) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConsoleInfraAccess.DeployEnvironmentKey] = environment })
            .Build());

    [Theory]
    [InlineData("dev", true)]
    [InlineData("DEV", true)]
    [InlineData("prod", false)]
    [InlineData(null, false)]
    public void Refusal_ShouldNameOnlyDeployedDev(string? environment, bool refused) =>
        (Access(environment).Refusal is not null).Should().Be(refused);

    [Fact]
    public async Task TheTailnetRead_ShouldAnswerProdOnlyOnDevWithoutAskingTheRunner()
    {
        var gateway = new UnreachableGateway();

        var result = await new TailnetReadQueryHandler(gateway, Access("dev")).HandleAsync(new TailnetReadQuery(), CancellationToken.None);

        var data = result.Should().BeOfType<AppResult<JsonElement>.Success>().Subject.Data;
        (data.GetProperty("prodOnly").GetBoolean(), data.GetProperty("connected").GetBoolean()).Should().Be((true, false));
        data.GetProperty("reason").GetString().Should().Be(ConsoleInfraAccess.DevRefusal);
    }

    [Fact]
    public async Task HostUpkeep_ShouldBeRefusedOnDevWithoutAskingTheRunner()
    {
        var result = await new ServerUpkeepCommandHandler(null!, new UnreachableGateway(), Access("dev"),
                new InfraFollowSchedule(TimeProvider.System))
            .HandleAsync(new ServerUpkeepCommand { Slug = "ocharo-vps", Actor = "sulton-max" }, CancellationToken.None);

        result.Should().BeOfType<AppResult<JsonElement>.Failure>().Subject.Error.Message
            .Should().Be(ConsoleInfraAccess.DevRefusal);
    }

    /// <summary>A gateway any call to which fails the test.</summary>
    private sealed class UnreachableGateway : IInfraGateway
    {
        private static Task<AppResult<JsonElement>> Never() => throw new InvalidOperationException("The runner was asked.");
        public Task<AppResult<JsonElement>> TailnetAsync(CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> PolicyAsync(CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> ApplyPolicyAsync(string etag, string digest, string actor, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> RevertPolicyAsync(string etag, string actor, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> EnrollmentAsync(string server, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> EnrollmentKeyAsync(string server, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> EnrollAsync(ServerEnrollmentRequest request, string actor, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> PlacementsAsync(string product, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> PlaceAsync(ProductPlacementRequest request, string actor, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> UpkeepsAsync(string server, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> UpkeepAsync(string server, string actor, CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> FinishedAsync(CancellationToken ct) => Never();
        public Task<AppResult<JsonElement>> AcknowledgeAsync(string job, CancellationToken ct) => Never();
    }
}
