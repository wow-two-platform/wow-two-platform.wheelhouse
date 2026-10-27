using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Services;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Tests.Unit.Fakes;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="ProductIdentityService"/>: the refresh decisions. The head is compared with the cached
/// commit; a different head reads the identity <c>deploy.yml</c> declares at it, or, while it declares none, the retired
/// <c>product/product.json</c> with a notice to move it. A valid identity replaces the row, an invalid one leaves the
/// previous row with a reason, none in either file drops the row, and an unreadable repository changes nothing.</summary>
public sealed class ProductIdentityServiceTests
{
    private const string Repository = "owner/pilot";
    private const string Descriptor = ProductIdentityService.DescriptorFile;
    private const string Retired = ProductIdentityService.RetiredFile;
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);
    private static readonly string Third = new('c', 40);

    private const string Services = """
        services:
          api:
            name: API
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
          worker:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health

        """;

    private readonly InMemoryProductManifests _store = new();
    private readonly ScriptedRepositoryFiles _files = new();
    private readonly ProductIdentityService _service;

    public ProductIdentityServiceTests() => _service = new ProductIdentityService(_store, _files);

    /// <summary>A descriptor that declares an identity.</summary>
    private static string Identity(string name, string description = "From the repository.") =>
        $"descriptor: 2\nproduct: pilot\nname: {name}\ndescription: {description}\n" +
        $"homepage: https://{name.ToLowerInvariant()}.example\n" + Services;

    /// <summary>A descriptor that declares no identity.</summary>
    private static string Bare => "descriptor: 2\nproduct: pilot\n" + Services.Replace("    name: API\n", "");

    /// <summary>A retired product/product.json.</summary>
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
        SourceFile = Descriptor, Name = name, Description = "Cached description.", Homepage = "https://cached.example",
    };

    private async Task<Wheelhouse.Application.Products.Models.ProductIdentity> ResolveAsync(ProductEntity product) =>
        (await _service.ResolveAsync([product], CancellationToken.None))[product.Id];

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheRecordedValues_WhenNeitherFileDeclaresAnIdentity()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Bare);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Description, identity.Homepage).Should().Be(("Recorded name", "Recorded description.", null));
        (identity.Source, identity.Commit, identity.Problem, identity.File, identity.Notice).Should().Be(
            (ProductIdentitySource.Record, null, null, null, null));
        _files.Reads.Should().Equal($"{Repository}@{First}:{Descriptor}", $"{Repository}@{First}:{Retired}");
        _store.Rows.Should().BeEmpty();
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheRecordedValues_WhenTheRepositoryCarriesNeitherFile()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, RepositoryFileStatus.Missing);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Problem, identity.Notice).Should().Be(
            ("Recorded name", ProductIdentitySource.Record, null, null));
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldStoreAndReturnTheDescriptorIdentity_WhenTheHeadDeclaresAValidOne()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Identity("Pilot"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Description, identity.Homepage).Should().Be(("Pilot", "From the repository.", "https://pilot.example"));
        (identity.Source, identity.Commit, identity.Problem, identity.File, identity.Notice).Should().Be(
            (ProductIdentitySource.Manifest, First, null, Descriptor, null));
        identity.Services.Select(service => (service.Service, service.Name, service.Description)).Should().Equal(("api", "API", null));
        var row = _store.Rows.Should().ContainSingle().Which.Value;
        (row.ProductId, row.Repository, row.Branch, row.Commit, row.SourceFile, row.Name).Should().Be(
            (product.Id, Repository, "main", First, Descriptor, "Pilot"));
        _files.Reads.Should().Equal($"{Repository}@{First}:{Descriptor}");
    }

    [Fact]
    public async Task ResolveAsync_ShouldReadTheRetiredFileWithANotice_WhenTheDescriptorDeclaresNoIdentity()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Bare);
        _files.Push(Repository, First, Retired, Manifest("Legacy"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Commit, identity.File).Should().Be(
            ("Legacy", ProductIdentitySource.Manifest, First, Retired));
        identity.Notice.Should().Be(ProductIdentityService.RetiredNotice);
        identity.Problem.Should().BeNull();
        _store.Rows[product.Id].SourceFile.Should().Be(Retired);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReadTheRetiredFile_WhenTheRepositoryHasNoDescriptor()
    {
        var product = Product();
        _files.Push(Repository, First, Retired, Manifest("Legacy"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.File, identity.Notice).Should().Be(("Legacy", Retired, ProductIdentityService.RetiredNotice));
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepTheNotice_WhenTheHeadIsTheCachedCommitOfARetiredFile()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _store.Rows[product.Id].SourceFile = Retired;
        _files.Push(Repository, First, Retired, Manifest("Changed"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.File, identity.Notice).Should().Be(("Cached name", Retired, ProductIdentityService.RetiredNotice));
        _files.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldIgnoreTheRetiredFile_WhenTheDescriptorDeclaresAnIdentity()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Identity("Described"));
        _files.Push(Repository, First, Retired, Manifest("Legacy"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.File, identity.Notice).Should().Be(("Described", Descriptor, null));
        _files.Reads.Should().NotContain(read => read.EndsWith(Retired, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotFallBackToTheRetiredFile_WhenTheDescriptorIdentityIsInvalid()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, "descriptor: 2\nproduct: pilot\nname: Pilot\n" + Services);
        _files.Push(Repository, First, Retired, Manifest("Legacy"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source).Should().Be(("Recorded name", ProductIdentitySource.Record));
        identity.Problem.Should().Be(
            "The identity in engineering/deployment/deploy.yml is ignored: line 1: description is required beside name.");
        _files.Reads.Should().NotContain(read => read.EndsWith(Retired, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_ShouldMoveTheRowToTheDescriptorAndDropTheNotice_WhenALaterCommitMovesTheIdentity()
    {
        var product = Product();
        _files.Push(Repository, First, Retired, Manifest("Legacy"));
        (await ResolveAsync(product)).Notice.Should().NotBeNull();
        _files.Push(Repository, Second, Descriptor, Identity("Moved"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit, identity.File, identity.Notice).Should().Be(("Moved", Second, Descriptor, null));
        _store.Rows[product.Id].SourceFile.Should().Be(Descriptor);
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotReadAFile_WhenTheHeadIsTheCachedCommit()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, First, Descriptor, Identity("Changed"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.File, identity.Notice).Should().Be(("Cached name", Descriptor, null));
        _files.Reads.Should().BeEmpty();
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReplaceTheRowWithTheNewerCommit_WhenTheHeadMovedAndTheIdentityIsValid()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, Second, Descriptor, Identity("Renamed"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit).Should().Be(("Renamed", Second));
        var row = _store.Rows.Should().ContainSingle().Which.Value;
        (row.Commit, row.Name, row.Homepage).Should().Be((Second, "Renamed", "https://renamed.example"));
        _files.Reads.Should().Equal($"{Repository}@{Second}:{Descriptor}");
    }

    [Fact]
    public async Task ResolveAsync_ShouldAdvanceTheCommit_WhenTheHeadMovedAndTheIdentityIsUnchanged()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Identity("Pilot"));
        await ResolveAsync(product);
        _files.Push(Repository, Second, Descriptor, Identity("Pilot"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit).Should().Be(("Pilot", Second));
        _store.Rows[product.Id].Commit.Should().Be(Second);
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepThePreviousRowAndReportWhy_WhenTheNewIdentityIsInvalid()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, Second, Descriptor, "descriptor: 2\nproduct: pilot\nname: \"\"\ndescription: x\n" + Services);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Commit).Should().Be(("Cached name", ProductIdentitySource.Manifest, First));
        identity.Problem.Should().Be(
            "The identity in engineering/deployment/deploy.yml is ignored: line 3: name must hold 1–80 characters.");
        _store.Rows[product.Id].Commit.Should().Be(First);
        _store.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheRecordAndReportWhy_WhenTheFirstDescriptorCannotBeParsed()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, "descriptor: [2\n");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source).Should().Be(("Recorded name", ProductIdentitySource.Record));
        identity.Problem.Should().StartWith("The identity in engineering/deployment/deploy.yml is ignored: line ")
            .And.EndWith("deploy.yml is not valid YAML.");
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheRecordAndReportWhy_WhenTheFirstRetiredFileIsInvalid()
    {
        var product = Product();
        _files.Push(Repository, First, Retired, "{ nope");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source, identity.Notice).Should().Be(("Recorded name", ProductIdentitySource.Record, null));
        identity.Problem.Should().Be("product/product.json is ignored: The file is not valid JSON (line 1).");
        _store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldClearTheReason_WhenALaterCommitIsValidAgain()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Identity("Pilot"));
        await ResolveAsync(product);
        _files.Push(Repository, Second, Descriptor, "descriptor: 2\nproduct: pilot\nhomepage: ftp://pilot.example\n" + Services);
        (await ResolveAsync(product)).Problem.Should().NotBeNull();
        _files.Push(Repository, Third, Descriptor, Identity("Repiloted"));

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit, identity.Problem).Should().Be(("Repiloted", Third, null));
    }

    [Fact]
    public async Task ResolveAsync_ShouldReportTheSizeLimit_WhenTheDescriptorIsTooLarge()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, Second, Descriptor, RepositoryFileStatus.TooLarge);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Problem).Should().Be(
            ("Cached name", "The identity in engineering/deployment/deploy.yml is ignored: The file is larger than 256 KiB."));
    }

    [Fact]
    public async Task ResolveAsync_ShouldReportTheSizeLimit_WhenTheRetiredFileIsTooLarge()
    {
        var product = Product();
        _files.Push(Repository, First, Retired, RepositoryFileStatus.TooLarge);

        var identity = await ResolveAsync(product);

        (identity.Source, identity.Problem).Should().Be(
            (ProductIdentitySource.Record, "product/product.json is ignored: The file is larger than 16 KiB."));
    }

    [Fact]
    public async Task ResolveAsync_ShouldDropTheRow_WhenTheNewerCommitDeclaresNoIdentity()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, Second, Descriptor, Bare);

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
        _files.Reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Descriptor)]
    [InlineData(Retired)]
    public async Task ResolveAsync_ShouldKeepTheRowWithoutAReason_WhenAFileCannotBeRead(string path)
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        if (path == Retired)
            _files.Push(Repository, Second, Descriptor, Bare);
        _files.Push(Repository, Second, path, RepositoryFileStatus.Unreadable);

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Commit, identity.Problem).Should().Be(("Cached name", First, null));
        _store.Deletes.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_ShouldKeepTheRowAndNotFail_WhenTheSourceThrows()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Failure = new InvalidOperationException("unexpected");

        var identity = await ResolveAsync(product);

        (identity.Name, identity.Source).Should().Be(("Cached name", ProductIdentitySource.Manifest));
    }

    [Fact]
    public async Task ResolveAsync_ShouldNotSwallowCancellation_WhenTheCallerCancels()
    {
        var product = Product();
        _files.Failure = new OperationCanceledException();
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
            ProductId = product.Id, Repository = "owner/pilot", Branch = "main", Commit = First, SourceFile = Descriptor,
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
        _files.Push(Repository, Second, Descriptor, Identity("Releasebranch"), branch: "release");

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
        _files.Push("Owner/Pilot", First, Descriptor, Identity("Changed"));

        var identity = await ResolveAsync(product);

        identity.Name.Should().Be("Cached name");
        _files.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldDecideEachProductOnItsOwn_WhenSeveralAreResolved()
    {
        var described = Product("alpha", "owner/alpha");
        var without = Product("beta", "owner/beta");
        var broken = Product("gamma", "owner/gamma");
        var retired = Product("delta", "owner/delta");
        _files.Push("owner/alpha", First, Descriptor, Identity("Alpha"));
        _files.Push("owner/beta", Second, Descriptor, Bare);
        _files.Push("owner/gamma", Third, Retired, "{ nope");
        _files.Push("owner/delta", First, Retired, Manifest("Delta"));

        var identities = await _service.ResolveAsync([described, without, broken, retired], CancellationToken.None);

        identities[described.Id].Name.Should().Be("Alpha");
        identities[without.Id].Name.Should().Be("Recorded name");
        (identities[broken.Id].Source, identities[broken.Id].Problem).Should().Be(
            (ProductIdentitySource.Record, "product/product.json is ignored: The file is not valid JSON (line 1)."));
        (identities[retired.Id].Name, identities[retired.Id].Notice).Should().Be(("Delta", ProductIdentityService.RetiredNotice));
        _store.Rows.Keys.Should().BeEquivalentTo([described.Id, retired.Id]);
    }

    [Fact]
    public async Task ResolveAsync_ShouldResolveNothing_WhenThereAreNoProducts()
    {
        (await _service.ResolveAsync([], CancellationToken.None)).Should().BeEmpty();

        _files.HeadChecks.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShouldLeaveTheHeadToTheSource_WhenAProductIsRead()
    {
        var product = Product();
        _files.Push(Repository, First, Descriptor, Identity("Pilot"));

        await ResolveAsync(product);
        await ResolveAsync(product);

        _files.HeadChecks.Should().OnlyContain(check => !check.Fresh);
        _files.HeadChecks.Should().HaveCount(2);
        _files.Reads.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshAsync_ShouldAskForAFreshHeadAndReadTheNewIdentity_WhenTheHeadMoved()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, Second, Descriptor, Identity("Fresh"));

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        (identity!.Name, identity.Commit).Should().Be(("Fresh", Second));
        _files.HeadChecks.Should().Equal((Repository, "main", true));
        _store.Rows[product.Id].Commit.Should().Be(Second);
    }

    [Fact]
    public async Task RefreshAsync_ShouldReadNothing_WhenTheHeadIsTheCachedCommit()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, First, Descriptor, Identity("Samecommit"));

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        identity!.Name.Should().Be("Cached name");
        _files.Reads.Should().BeEmpty();
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
    public async Task RefreshAsync_ShouldReturnTheReason_WhenTheNewestIdentityIsInvalid()
    {
        var product = Product();
        _store.Rows[product.Id] = Row(product, First);
        _files.Push(Repository, Second, Descriptor, "descriptor: 2\nproduct: pilot\nname: Pilot\n" + Services);

        var identity = await _service.RefreshAsync(product, CancellationToken.None);

        (identity!.Name, identity.Problem).Should().Be(
            ("Cached name", "The identity in engineering/deployment/deploy.yml is ignored: line 1: description is required beside name."));
    }
}
