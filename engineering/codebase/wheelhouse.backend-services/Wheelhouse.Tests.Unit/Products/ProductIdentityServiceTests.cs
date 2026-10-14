using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Services;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="ProductIdentityService"/>: the refresh decisions. The head is compared with the cached
/// commit; a different head reads the manifest at it; a valid file replaces the row, an invalid one leaves the previous
/// row with a reason, a missing one drops the row, and an unreadable repository changes nothing.</summary>
public sealed class ProductIdentityServiceTests
{
    private const string Repository = "owner/pilot";
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);
    private static readonly string Third = new('c', 40);

    private readonly InMemoryProductManifests _store = new();
    private readonly ScriptedManifestSource _source = new();
    private readonly ProductIdentityService _service;

    public ProductIdentityServiceTests() => _service = new ProductIdentityService(_store, _source);

    private static string Manifest(string name, string description = "From the repository.") =>
        $$"""{ "manifest": 1, "name": "{{name}}", "description": "{{description}}", "homepage": "https://{{name.ToLowerInvariant()}}.example" }""";

    private static ProductEntity Product(string slug = "pilot", string repository = Repository, string branch = "main") => new()
    {
        Id = Guid.NewGuid(), Slug = slug, Name = "Recorded name", Description = "Recorded description.",
        Repository = repository, DefaultBranch = branch,
    };

    private static ProductManifestEntity Row(ProductEntity product, string commit, string name = "Cached name") => new()
    {
        ProductId = product.Id, Repository = product.Repository, Branch = product.DefaultBranch, Commit = commit,
        Name = name, Description = "Cached description.", Homepage = "https://cached.example",
    };

    private async Task<Wheelhouse.Application.Products.Models.ProductIdentity> ResolveAsync(ProductEntity product) =>
        (await _service.ResolveAsync([product], CancellationToken.None))[product.Id];

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheRecordedValues_WhenTheRepositoryCarriesNoManifest()
    {
        var product = Product();
        _source.Push(Repository, First, ProductManifestFileStatus.Missing);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Description, identity.Homepage).Should().Be(("Recorded name", "Recorded description.", null));
        (identity.Source, identity.Commit, identity.Problem).Should().Be((ProductIdentitySource.Record, null, null));
        _store.Rows.Should().BeEmpty();
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldStoreAndReturnTheManifest_WhenTheHeadHoldsAValidOne()
    {
        var product = Product();
        _source.Push(Repository, First, Manifest("Pilot"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Description, identity.Homepage).Should().Be(("Pilot", "From the repository.", "https://pilot.example"));
        (identity.Source, identity.Commit, identity.Problem).Should().Be((ProductIdentitySource.Manifest, First, null));
        var row = _store.Rows.Should().ContainSingle().Which.Value;
        (row.ProductId, row.Repository, row.Branch, row.Commit, row.Name).Should().Be((product.Id, Repository, "main", First, "Pilot"));
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotReadTheFile_WhenTheHeadIsTheCachedCommit()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, First, Manifest("Changed"));

        var identity = await ResolveAsync(product);

        identity.Name.Should().Be("Cached name");
        _source.Reads.Should().BeEmpty();
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReplaceTheRowWithTheNewerCommit_WhenTheHeadMovedAndTheManifestIsValid()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, Manifest("Renamed"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit).Should().Be(("Renamed", Second));
        var row = _store.Rows.Should().ContainSingle().Which.Value;
        (row.Commit, row.Name, row.Homepage).Should().Be((Second, "Renamed", "https://renamed.example"));
        _source.Reads.Should().Equal($"{Repository}@{Second}");
    }

    [Fact]
    public async Task ResolveAsync_ShouldAdvanceTheCommit_WhenTheHeadMovedAndTheManifestIsUnchanged()
    {
        var product = Product();
        _source.Push(Repository, First, Manifest("Pilot"));
        await ResolveAsync(product);
        _source.Push(Repository, Second, Manifest("Pilot"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit).Should().Be(("Pilot", Second));
        _store.Rows[product.Id].Commit.Should().Be(Second);
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepThePreviousRowAndReportWhy_WhenTheNewManifestIsInvalid()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, """{ "manifest": 1, "name": "", "description": "x" }""");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Commit).Should().Be(("Cached name", ProductIdentitySource.Manifest, First));
        identity.Problem.Should().Be("'name' must hold 1–80 characters.");
        _store.Rows[product.Id].Commit.Should().Be(First);
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheRecordAndReportWhy_WhenTheFirstManifestIsInvalid()
    {
        var product = Product();
        _source.Push(Repository, First, "{ nope");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source).Should().Be(("Recorded name", ProductIdentitySource.Record));
        identity.Problem.Should().StartWith("The file is not valid JSON");
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldClearTheReason_WhenALaterCommitIsValidAgain()
    {
        var product = Product();
        _source.Push(Repository, First, Manifest("Pilot"));
        await ResolveAsync(product);
        _source.Push(Repository, Second, "{ nope");
        (await ResolveAsync(product)).Problem.Should().NotBeNull();
        _source.Push(Repository, Third, Manifest("Repiloted"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit, identity.Problem).Should().Be(("Repiloted", Third, null));
    }

    [Fact]
    public async Task ResolveAsync_ShouldReportTheSizeLimit_WhenTheFileIsTooLarge()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, ProductManifestFileStatus.TooLarge);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Problem).Should().Be(("Cached name", "The file is larger than 16 KiB."));
    }

    [Fact]
    public async Task ResolveAsync_ShouldDropTheRow_WhenTheNewerCommitHasNoManifest()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, ProductManifestFileStatus.Missing);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Problem).Should().Be(("Recorded name", ProductIdentitySource.Record, null));
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepTheRowWithoutAReason_WhenTheBranchCannotBeRead()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Problem).Should().Be(("Cached name", ProductIdentitySource.Manifest, null));
        _store.Rows.Should().ContainKey(product.Id);
        _source.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepTheRowWithoutAReason_WhenTheFileCannotBeRead()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, ProductManifestFileStatus.Unreadable);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit, identity.Problem).Should().Be(("Cached name", First, null));
        _store.Deletes.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepTheRowAndNotFail_WhenTheSourceThrows()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Failure = new InvalidOperationException("unexpected");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source).Should().Be(("Cached name", ProductIdentitySource.Manifest));
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotSwallowCancellation_WhenTheCallerCancels()
    {
        var product = Product();
        _source.Failure = new OperationCanceledException();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => _service.ResolveAsync([product], cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldIgnoreAndDropARowFromAnotherRepository_WhenTheProductMoved()
    {
        var product = Product(repository: "owner/moved");
        _store.Rows[product.Id] = new ProductManifestEntity
        {
            ProductId = product.Id, Repository = "owner/pilot", Branch = "main", Commit = First,
            Name = "Old repo name", Description = "x",
        };

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source).Should().Be(("Recorded name", ProductIdentitySource.Record));
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldIgnoreARowFromAnotherBranch_WhenTheDefaultBranchChanged()
    {
        var product = Product(branch: "release");
        _store.Rows[product.Id] = Row(product, First);
        _store.Rows[product.Id].Branch = "main";
        _source.Push(Repository, Second, Manifest("Releasebranch"), branch: "release");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit).Should().Be(("Releasebranch", Second));
        _store.Rows[product.Id].Branch.Should().Be("release");
    }

    [Fact]
    public async Task ResolveAsync_ShouldMatchTheRepositoryIgnoringCase_WhenTheRowWasReadUnderAnotherCasing()
    {
        var product = Product(repository: "Owner/Pilot");
        _store.Rows[product.Id] = Row(product, First);
        _store.Rows[product.Id].Repository = "owner/pilot";
        _source.Push("Owner/Pilot", First, Manifest("Changed"));

        var identity = await ResolveAsync(product);

        identity.Name.Should().Be("Cached name");
        _source.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldDecideEachProductOnItsOwn_WhenSeveralAreResolved()
    {
        var withManifest = Product("alpha", "owner/alpha");
        var without = Product("beta", "owner/beta");
        var broken = Product("gamma", "owner/gamma");
        _source.Push("owner/alpha", First, Manifest("Alpha"));
        _source.Push("owner/beta", Second, ProductManifestFileStatus.Missing);
        _source.Push("owner/gamma", Third, "{ nope");

        var identities = await _service.ResolveAsync([withManifest, without, broken], CancellationToken.None);

        identities[withManifest.Id].Name.Should().Be("Alpha");
        identities[without.Id].Name.Should().Be("Recorded name");
        (identities[broken.Id].Source, identities[broken.Id].Problem).Should().Be((ProductIdentitySource.Record, "The file is not valid JSON (line 1)."));
        _store.Rows.Keys.Should().BeEquivalentTo([withManifest.Id]);
    }

    [Fact]
    public async Task ResolveAsync_ShouldResolveNothing_WhenThereAreNoProducts()
    {
        (await _service.ResolveAsync([], CancellationToken.None)).Should().BeEmpty();

        _source.HeadChecks.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldLeaveTheHeadToTheSource_WhenAProductIsRead()
    {
        var product = Product();
        _source.Push(Repository, First, Manifest("Pilot"));

        await ResolveAsync(product);
        await ResolveAsync(product);

        _source.HeadChecks.Should().OnlyContain(check => !check.Fresh);
        _source.HeadChecks.Should().HaveCount(2);
        _source.Reads.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshAsync_ShouldAskForAFreshHeadAndReadTheNewManifest_WhenTheHeadMoved()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, Manifest("Fresh"));

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        (identity!.Name, identity.Commit).Should().Be(("Fresh", Second));
        _source.HeadChecks.Should().Equal((Repository, "main", true));
        _store.Rows[product.Id].Commit.Should().Be(Second);
    }

    [Fact]
    public async Task RefreshAsync_ShouldReadNothing_WhenTheHeadIsTheCachedCommit()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, First, Manifest("Samecommit"));

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        identity!.Name.Should().Be("Cached name");
        _source.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_ShouldReturnNothingAndKeepTheRow_WhenTheBranchCannotBeRead()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        identity.Should().BeNull();
        _store.Rows[product.Id].Commit.Should().Be(First);
    }

    [Fact]
    public async Task RefreshAsync_ShouldReturnTheReason_WhenTheNewestManifestIsInvalid()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _source.Push(Repository, Second, """{ "manifest": 1, "name": "Pilot" }""");

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        (identity!.Name, identity.Problem).Should().Be(("Cached name", "'description' is required."));
    }
}
