using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for product identity: the identity a product's <c>deploy.yml</c> declares shows as its name, description and
/// homepage; the projection is persisted with the commit and file it was read from and replaced only by a newer commit;
/// an invalid identity is ignored with a reason while the previous projection stays; a descriptor without one falls back
/// to a retired <c>product/product.json</c> with a notice to move it, and a repository with neither to the product's own
/// row; and an explicit refresh asks for the head again.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ProductIdentityE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string ForeverPin = "sulton-max/10x-venture-forever-pin";
    private const string Wheelhouse = "wow-two-platform/wow-two-platform.wheelhouse";
    private const string DescriptorPath = "engineering/deployment/deploy.yml";
    private const string RetiredPath = "product/product.json";
    private const string RetiredNotice =
        "The name and description come from product/product.json, which is retired: move them into " +
        "engineering/deployment/deploy.yml (name, description, homepage) and delete the file.";

    private const string Services = """
        services:
          management:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
          redirect:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health

        """;

    /// <summary>A descriptor that declares no identity.</summary>
    private const string Bare = "descriptor: 2\nproduct: foreverpin\n" + Services;

    /// <summary>A descriptor that declares an identity; <paramref name="extra"/> adds top-level lines.</summary>
    private static string Identity(string name, string description = "From the repository.", string extra = "") =>
        "descriptor: 2\nproduct: foreverpin\nname: " + name + "\ndescription: " + description + "\n" + extra + Services;

    /// <summary>A retired product/product.json.</summary>
    private static string Manifest(string name, string description = "From the repository.", string extra = "") =>
        "{ \"manifest\": 1, \"name\": \"" + name + "\", \"description\": \"" + description + "\"" + extra + " }";

    private string Push(string repository, string descriptor) =>
        Fixture.GitHub.Push(repository, DescriptorPath, descriptor);

    private int DescriptorReads(string repository) =>
        Fixture.GitHub.FileReads.Count(read => read.StartsWith(repository + "@", StringComparison.OrdinalIgnoreCase)
            && read.EndsWith(":" + DescriptorPath, StringComparison.Ordinal));

    private IEnumerable<string> HeadChecks(string repository) =>
        Fixture.GitHub.HeadReads.Where(read => read.StartsWith(repository + "@", StringComparison.OrdinalIgnoreCase));

    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<ProductResponse> GetAsync(string slug) =>
        await (await AdminClient.GetAsync($"api/products/{slug}")).ReadEnvelopeAsync<ProductResponse>();

    private async Task<IReadOnlyList<ProductManifestEntity>> RowsAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var products = scope.ServiceProvider.GetRequiredService<IProductsRepository>();
        var manifests = scope.ServiceProvider.GetRequiredService<IProductManifestsRepository>();
        var all = await products.GetAllAsync();
        return [.. (await manifests.GetByProductsAsync([.. all.Select(product => product.Id)])).Values];
    }

    private async Task<string> RecordedNameAsync(string slug)
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IProductsRepository>().GetBySlugAsync(slug))!.Name;
    }

    [Fact]
    public async Task GetAll_ShouldShowTheDescriptorIdentityAndPersistTheProjection_WhenTheRepositoryDeclaresOne()
    {
        var commit = Push(ForeverPin, Identity("Forever Pin", "QR codes that outlive the print.", "homepage: https://foreverpin.example\n")
            .Replace("  management:\n", "  management:\n    name: Editor\n    description: Edits codes.\n")
            .Replace("  redirect:\n", "  redirect:\n    name: Redirect\n"));

        var products = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();

        var foreverPin = products.Single(product => product.Slug == "foreverpin");
        (foreverPin.Name, foreverPin.Description, foreverPin.Homepage).Should().Be(
            ("Forever Pin", "QR codes that outlive the print.", "https://foreverpin.example"));
        (foreverPin.Identity!.Source, foreverPin.Identity.Commit, foreverPin.Identity.File).Should().Be(
            ("manifest", commit, DescriptorPath));
        (foreverPin.Identity.Problem, foreverPin.Identity.Notice).Should().Be((null, null));
        foreverPin.Identity.Services.Should().Equal(
            new ProductServiceResponse("management", "Editor", "Edits codes."),
            new ProductServiceResponse("redirect", "Redirect", null));

        // The other product's repository declares none, so it reads as its own row.
        var wheelhouse = products.Single(product => product.Slug == "wheelhouse");
        (wheelhouse.Name, wheelhouse.Homepage, wheelhouse.Identity!.Source, wheelhouse.Identity.Commit, wheelhouse.Identity.File)
            .Should().Be(("Wheelhouse", null, "record", null, null));

        var row = (await RowsAsync()).Should().ContainSingle().Which;
        (row.Repository, row.Branch, row.Commit, row.SourceFile, row.Name, row.Homepage).Should().Be(
            (ForeverPin, "main", commit, DescriptorPath, "Forever Pin", "https://foreverpin.example"));
        row.Services.Select(service => service.Service).Should().Equal("management", "redirect");
    }

    [Fact]
    public async Task GetBySlug_ShouldShowTheRetiredFileWithANotice_WhenTheDescriptorDeclaresNoIdentity()
    {
        Push(ForeverPin, Bare);
        var commit = Fixture.GitHub.Push(ForeverPin, RetiredPath, Manifest("Legacy Pin", "From the retired file."));

        var product = await GetAsync("foreverpin");

        (product.Name, product.Description, product.Identity!.Source, product.Identity.Commit, product.Identity.File).Should().Be(
            ("Legacy Pin", "From the retired file.", "manifest", commit, RetiredPath));
        (product.Identity.Notice, product.Identity.Problem).Should().Be((RetiredNotice, null));
        (await RowsAsync()).Should().ContainSingle().Which.SourceFile.Should().Be(RetiredPath);
    }

    [Fact]
    public async Task GetBySlug_ShouldDropTheNotice_WhenALaterCommitMovesTheIdentityIntoTheDescriptor()
    {
        Fixture.GitHub.Push(ForeverPin, RetiredPath, Manifest("Legacy Pin"));
        (await GetAsync("foreverpin")).Identity!.Notice.Should().Be(RetiredNotice);

        Fixture.GitHub.Push(ForeverPin, RetiredPath, FileRead.NotFound);
        var moved = Push(ForeverPin, Identity("Forever Pin"));
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Commit, product.Identity.File, product.Identity.Notice).Should().Be(
            ("Forever Pin", moved, DescriptorPath, null));
        (await RowsAsync()).Should().ContainSingle().Which.SourceFile.Should().Be(DescriptorPath);
    }

    [Fact]
    public async Task GetBySlug_ShouldPreferTheDescriptor_WhenBothFilesDeclareAnIdentity()
    {
        Fixture.GitHub.Push(ForeverPin, RetiredPath, Manifest("Legacy Pin"));
        Push(ForeverPin, Identity("Forever Pin"));

        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.File, product.Identity.Notice).Should().Be(("Forever Pin", DescriptorPath, null));
        Fixture.GitHub.FileReads.Should().NotContain(read => read.EndsWith(":" + RetiredPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetBySlug_ShouldReadTheIdentityOnce_WhenTheHeadHasNotMoved()
    {
        Push(ForeverPin, Identity("Forever Pin"));

        await GetAsync("foreverpin");
        var reads = DescriptorReads(ForeverPin);
        await GetAsync("foreverpin");
        await GetAsync("foreverpin");

        DescriptorReads(ForeverPin).Should().Be(reads, "a head the cache already holds reads no file");
        HeadChecks(ForeverPin).Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetBySlug_ShouldReplaceTheRowWithTheNewerCommit_WhenTheHeadMoves()
    {
        var first = Push(ForeverPin, Identity("Forever Pin"));
        (await GetAsync("foreverpin")).Name.Should().Be("Forever Pin");

        var second = Push(ForeverPin, Identity("Forever Pin Plus", "Now with more."));
        var product = await GetAsync("foreverpin");

        (product.Name, product.Description, product.Identity!.Commit).Should().Be(("Forever Pin Plus", "Now with more.", second));
        var row = (await RowsAsync()).Should().ContainSingle().Which;
        (row.Commit, row.Name).Should().Be((second, "Forever Pin Plus"));
        row.Commit.Should().NotBe(first);
    }

    [Fact]
    public async Task GetBySlug_ShouldAdvanceTheCommit_WhenOnlyOtherFilesChanged()
    {
        Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");

        var second = Fixture.GitHub.Push(ForeverPin, "README.md", "Changed.");
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Commit).Should().Be(("Forever Pin", second));
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(second);
    }

    [Fact]
    public async Task GetBySlug_ShouldKeepThePreviousProjectionAndReportWhy_WhenTheNewestIdentityIsInvalid()
    {
        var valid = Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");

        Push(ForeverPin, Identity(new string('n', 81)));
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Commit).Should().Be(("Forever Pin", "manifest", valid));
        product.Identity.Problem.Should().Be(
            "The identity in engineering/deployment/deploy.yml is ignored: line 3: name must hold 1–80 characters.");
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(valid);

        // A later valid commit lifts the reason.
        var fixedCommit = Push(ForeverPin, Identity("Forever Pin Again"));
        var repaired = await GetAsync("foreverpin");
        (repaired.Name, repaired.Identity!.Commit, repaired.Identity.Problem).Should().Be(("Forever Pin Again", fixedCommit, null));
    }

    [Theory]
    [InlineData("descriptor: 2\nproduct: foreverpin\nname: Pilot\n",
        "The identity in engineering/deployment/deploy.yml is ignored: line 1: description is required beside name.")]
    [InlineData("descriptor: 2\nproduct: foreverpin\nname: Pilot\ndescription: x\nhomepage: javascript:alert(1)\n",
        "The identity in engineering/deployment/deploy.yml is ignored: line 5: homepage must be an http or https address without credentials, at most 300 characters.")]
    [InlineData("descriptor: 2\nproduct: foreverpin\nname: [Pilot]\ndescription: x\n",
        "The identity in engineering/deployment/deploy.yml is ignored: line 3: name must be text.")]
    public async Task GetBySlug_ShouldFallBackToTheRecordAndReportWhy_WhenTheFirstIdentityIsInvalid(string head, string reason)
    {
        Push(ForeverPin, head + Services);
        Fixture.GitHub.Push(ForeverPin, RetiredPath, Manifest("Legacy Pin"));

        var product = await GetAsync("foreverpin");

        (product.Name, product.Description, product.Homepage).Should().Be(("ForeverPin", "Styled QR codes and short links.", null));
        (product.Identity!.Source, product.Identity.Commit, product.Identity.Problem).Should().Be(("record", null, reason));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("{ nope", "product/product.json is ignored: The file is not valid JSON (line 1).")]
    [InlineData("[]", "product/product.json is ignored: The file must hold one JSON object.")]
    [InlineData("{ \"manifest\": 1, \"name\": 5, \"description\": \"x\" }", "product/product.json is ignored: 'name' must be text.")]
    public async Task GetBySlug_ShouldFallBackToTheRecordAndReportWhy_WhenTheRetiredFileIsInvalid(string file, string reason)
    {
        Fixture.GitHub.Push(ForeverPin, RetiredPath, file);

        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Problem, product.Identity.Notice).Should().Be(
            ("ForeverPin", "record", reason, null));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySlug_ShouldReportTheSizeLimit_WhenTheDescriptorIsTooLarge()
    {
        Push(ForeverPin, Identity("Forever Pin") + "# " + new string('x', 256 * 1024) + "\n");

        var product = await GetAsync("foreverpin");

        product.Identity!.Problem.Should().Be(
            "The identity in engineering/deployment/deploy.yml is ignored: The file is larger than 256 KiB.");
        product.Identity.Source.Should().Be("record");
    }

    [Fact]
    public async Task GetBySlug_ShouldDropTheProjection_WhenTheNewestCommitDeclaresNoIdentity()
    {
        Push(ForeverPin, Identity("Forever Pin"));
        (await GetAsync("foreverpin")).Identity!.Source.Should().Be("manifest");
        (await RowsAsync()).Should().HaveCount(1);

        Push(ForeverPin, Bare);
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Problem, product.Identity.File).Should().Be(
            ("ForeverPin", "record", null, null));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySlug_ShouldKeepTheProjectionWithoutAReason_WhenTheRepositoryCannotBeRead()
    {
        var commit = Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");

        Fixture.GitHub.Unreachable(ForeverPin);
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Commit, product.Identity.Problem).Should().Be(
            ("Forever Pin", "manifest", commit, null));
        (await RowsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetBySlug_ShouldKeepTheProjection_WhenTheFileCannotBeRead()
    {
        var commit = Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");

        Fixture.GitHub.Push(ForeverPin, DescriptorPath, FileRead.Failed);
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Commit, product.Identity.Problem).Should().Be(("Forever Pin", commit, null));
    }

    [Fact]
    public async Task GetAll_ShouldNeverFail_WhenNoRepositoryCanBeRead()
    {
        var response = await AdminClient.GetAsync("api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>()).Should().OnlyContain(
            product => product.Identity!.Source == "record");
    }

    [Fact]
    public async Task GetAll_ShouldLeaveNoAuditEntry_WhenTheProjectionIsFilled()
    {
        Push(ForeverPin, Identity("Forever Pin"));

        await AdminClient.GetAsync("api/products");

        (await AdminClient.AuditItemsAsync()).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturnTheProductAtTheNewHead_WhenTheOwnerRefreshes()
    {
        Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");
        var second = Push(ForeverPin, Identity("Forever Pin Plus"));

        var response = await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Identity!.Commit).Should().Be(("Forever Pin Plus", second));
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(second);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturnTheReason_WhenTheNewestIdentityIsInvalid()
    {
        var valid = Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");
        Push(ForeverPin, "descriptor: 2\nproduct: foreverpin\nname: Pin\n" + Services);

        var response = await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Identity!.Commit, product.Identity.Problem).Should().Be(("Forever Pin", valid,
            "The identity in engineering/deployment/deploy.yml is ignored: line 1: description is required beside name."));
    }

    [Fact]
    public async Task RefreshIdentity_ShouldFailAndKeepTheProjection_WhenTheBranchCannotBeRead()
    {
        var commit = Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");
        Fixture.GitHub.Unreachable(ForeverPin);

        var response = await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.ReadProblemAsync()).Detail.Should().Contain("could not be read from GitHub");
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(commit);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldBeAudited_WhenTheOwnerRefreshes()
    {
        Push(ForeverPin, Identity("Forever Pin"));

        await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        // The refresh also brings the approved services up to deploy.yml, which audits on its own (product.services).
        var entry = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(item => item.GetProperty("action").GetString() == "product.refresh").Should().ContainSingle().Which;
        (entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString())
            .Should().Be(("foreverpin", "succeeded"));
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fixture.GitHub.HeadReads.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturn404_WhenTheCatalogLacksTheProduct()
    {
        var response = await ActionClient("refresh").PostAsync("api/products/ghost/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturn400_WhenTheSlugIsMalformed()
    {
        var response = await ActionClient("refresh").PostAsync("api/products/Forever_Pin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturn401_WhenTheCallerIsAnonymous()
    {
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "refresh");

        var response = await client.PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_ShouldLeaveTheRowsNameAlone_WhenTheRepositoryDeclaresTheIdentity()
    {
        Push(ForeverPin, Identity("Forever Pin", "From the repository."));

        var response = await ActionClient("product").PutJsonAsync("api/products/foreverpin", new
        {
            name = "Edited in the console",
            description = "Edited too.",
            repository = ForeverPin,
            defaultBranch = "main",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Description, product.Identity!.Source).Should().Be(("Forever Pin", "From the repository.", "manifest"));
        (await RecordedNameAsync("foreverpin")).Should().Be("ForeverPin");
    }

    [Fact]
    public async Task Update_ShouldSaveTheName_WhenTheRepositoryDeclaresNoIdentity()
    {
        var response = await ActionClient("product").PutJsonAsync("api/products/foreverpin", new
        {
            name = "Renamed in the console",
            description = "Edited here.",
            repository = ForeverPin,
            defaultBranch = "main",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Description, product.Identity!.Source).Should().Be(("Renamed in the console", "Edited here.", "record"));
    }

    [Fact]
    public async Task Update_ShouldDropTheStaleProjectionAndSaveTheName_WhenTheRepositoryChanges()
    {
        Push(ForeverPin, Identity("Forever Pin"));
        await GetAsync("foreverpin");
        (await RowsAsync()).Should().HaveCount(1);

        var response = await ActionClient("product").PutJsonAsync("api/products/foreverpin", new
        {
            name = "Moved",
            description = "Lives elsewhere now.",
            repository = "sulton-max/moved-repo",
            defaultBranch = "main",
        });

        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Identity!.Source).Should().Be(("Moved", "record"));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Update_ShouldReadTheNewRepositorysIdentity_WhenTheRepositoryChanges()
    {
        Push("sulton-max/moved-repo", Identity("Moved Pin"));

        var response = await ActionClient("product").PutJsonAsync("api/products/foreverpin", new
        {
            name = "Typed name",
            description = "Typed description.",
            repository = "sulton-max/moved-repo",
            defaultBranch = "main",
        });

        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Identity!.Source).Should().Be(("Moved Pin", "manifest"));
        (await RowsAsync()).Should().ContainSingle().Which.Repository.Should().Be("sulton-max/moved-repo");
    }

    [Fact]
    public async Task Create_ShouldShowTheIdentity_WhenTheNewProductsRepositoryDeclaresOne()
    {
        Push("owner/newcomer", Identity("Newcomer", "Fresh from its repository.", "homepage: https://newcomer.example\n")
            .Replace("product: foreverpin", "product: newcomer"));

        var response = await ActionClient("product").PostJsonAsync("api/products", new
        {
            slug = "newcomer",
            name = "Typed at creation",
            description = "Typed.",
            repository = "owner/newcomer",
            defaultBranch = "main",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Description, product.Homepage, product.Identity!.Source).Should().Be(
            ("Newcomer", "Fresh from its repository.", "https://newcomer.example", "manifest"));
        (await RecordedNameAsync("newcomer")).Should().Be("Typed at creation");
    }

    [Fact]
    public async Task Delete_ShouldRemoveTheProjectionWithTheProduct_WhenTheProductGoes()
    {
        Push(Wheelhouse, Identity("Wheelhouse").Replace("product: foreverpin", "product: wheelhouse"));
        await GetAsync("wheelhouse");
        (await RowsAsync()).Should().HaveCount(1);

        var response = await ActionClient("product").DeleteAsync("api/products/wheelhouse");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_ShouldGiveEveryProductItsOwnIdentity_WhenOnlyOneRepositoryDeclaresAValidOne()
    {
        Push(Wheelhouse, Identity("Wheelhouse Console", "The deploy console.").Replace("product: foreverpin", "product: wheelhouse"));
        Fixture.GitHub.Push(ForeverPin, RetiredPath, "{ nope");

        var products = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();

        products.Select(product => (product.Slug, product.Name, product.Identity!.Source)).Should().Equal(
            ("foreverpin", "ForeverPin", "record"), ("wheelhouse", "Wheelhouse Console", "manifest"));
        products[0].Identity!.Problem.Should().NotBeNull();
        products[1].Identity!.Problem.Should().BeNull();
    }
}
