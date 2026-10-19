using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Vaults.Entities;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Settings;

/// <summary>
/// Tests for <see cref="ExternalEnvironmentSecrets"/>, the fallback for one release after the merge: an environment's
/// secrets live in the vault on its target's server, else the inventory's first, under <c>{product}-{environment}</c>;
/// disabled secrets list as absent, an unreachable vault says it cannot tell, a first secret creates its namespace, and a
/// removal disables.
/// </summary>
public sealed class ExternalEnvironmentSecretsTests
{
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly ProductEntity Product = new()
    {
        Id = Guid.NewGuid(), Slug = "pilot", Name = "Pilot", Description = "", Repository = "owner/pilot", DefaultBranch = "main",
        WorkspaceId = Guid.NewGuid(),
    };
    private static readonly TargetEntity Target = new()
    {
        Id = Guid.NewGuid(), Slug = "pilot-prod", ProductId = Product.Id, ServerId = Server, Environment = DeploymentEnvironment.Prod,
        Network = "platform", Root = "/srv/wheelhouse",
    };
    private readonly ScriptedVault _vault = new();
    private readonly InMemoryVaultsRepository _vaults = new();

    private ExternalEnvironmentSecrets Secrets() => new(_vaults, _vault);

    private void Vault(string slug, Guid server) =>
        _vaults.Rows.Add(new VaultEntity { Id = Guid.NewGuid(), Slug = slug, Name = slug, ServerId = server, Url = "http://vault:8080" });

    [Fact]
    public async Task APlace_ShouldBeTheTargetServersVault_ElseTheFirst_OrNone()
    {
        (await Secrets().PlaceAsync(Target, Product, default)).Should().BeNull("the inventory has no vault");
        Vault("b-vault", Guid.NewGuid());
        Vault("a-vault", Guid.NewGuid());
        (await Secrets().VaultOfAsync(Target, Product, default)).Should().Be(("a-vault", "pilot-prod"));
        Vault("own-vault", Server);
        var place = await Secrets().PlaceAsync(Target, Product, default);
        (place!.Id, place.Namespace, place.Mode, place.Reachable).Should().Be(("own-vault", "pilot-prod", "external", true));
    }

    [Fact]
    public async Task Listing_ShouldLeaveDisabledSecretsOut_AndSayWhenTheVaultCannotTell()
    {
        Vault("own-vault", Server);
        _vault.Secrets = JsonSerializer.SerializeToElement(new object[]
        {
            new { key = "A:Key", state = "active", version = 3, updatedAtUtc = "2026-10-19T10:00:00Z" },
            new { key = "B:Key", state = "disabled", version = 1, updatedAtUtc = "2026-10-19T10:00:00Z" },
        });
        var listed = await Secrets().ListAsync(Target, Product, default);
        listed!.Keys.Should().Equal("A:Key");
        listed["A:Key"].Version.Should().Be(3);

        _vault.Unreachable = true;
        (await Secrets().ListAsync(Target, Product, default)).Should().BeNull();
    }

    [Fact]
    public async Task AFirstWrite_ShouldCreateTheNamespace_AndARemovalDisables()
    {
        Vault("own-vault", Server);
        _vault.MissingNamespace = true;
        var written = await Secrets().WriteAsync(Target, Product, [new SecretWrite("A:Key", "value", "first")], "owner", default);
        written.Should().BeOfType<AppResult<IReadOnlyList<SecretWritten>>.Success>().Subject.Data.Single().Version.Should().Be(4);
        _vault.Changes.Select(change => change.GetType().Name).Should().Equal(
            nameof(SecretSetChange), nameof(NamespaceCreateChange), nameof(SecretSetChange));

        _vault.Secrets = JsonSerializer.SerializeToElement(new object[] { new { key = "A:Key", state = "active", version = 4 } });
        var removed = await Secrets().RemoveAsync(Target, Product, ["A:Key", "Gone:Key"], default);
        removed.Should().BeOfType<AppResult<IReadOnlyDictionary<string, int>>.Success>().Subject.Data.Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["A:Key"] = 4 });
        _vault.Changes.Last().Should().BeOfType<SecretStateChange>().Which.Disabled.Should().BeTrue();
    }

    /// <summary>A standalone vault scripted in memory.</summary>
    private sealed class ScriptedVault : IVaultGateway
    {
        public JsonElement Secrets { get; set; } = JsonSerializer.SerializeToElement(Array.Empty<object>());
        public bool Unreachable { get; set; }
        public bool MissingNamespace { get; set; }
        public List<VaultChange> Changes { get; } = [];

        public Task<AppResult<JsonElement>> ReadAsync(VaultResource resource, string? vault, string? ns, CancellationToken ct) =>
            Task.FromResult(Unreachable
                ? AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable("unreachable"))
                : AppResult<JsonElement>.Ok(Secrets));

        public Task<AppResult<JsonElement>> ApplyAsync(string vault, VaultChange change, CancellationToken ct)
        {
            Changes.Add(change);
            if (change is SecretSetChange && MissingNamespace)
            {
                MissingNamespace = false;
                return Task.FromResult(AppResult<JsonElement>.Fail(AppErrorFactory.NotFound("no namespace")));
            }
            return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new { version = 4 })));
        }
    }
}
