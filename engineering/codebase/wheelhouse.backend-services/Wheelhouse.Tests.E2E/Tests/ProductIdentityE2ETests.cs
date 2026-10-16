using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for product identity: the cached manifest of a product's repository shows as its name, description and homepage;
/// the projection is persisted with the commit it was read from and replaced only by a newer commit; an invalid manifest
/// is ignored with a reason while the previous projection stays; a repository without a manifest falls back to the
/// product's own row; and an explicit refresh asks for the head again.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ProductIdentityE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string ForeverPin = "sulton-max/10x-venture-forever-pin";
    private const string Wheelhouse = "wow-two-platform/wow-two-platform.wheelhouse";

    private static string Manifest(string name, string description = "From the repository.", string extra = "") =>
        "{ \"manifest\": 1, \"name\": \"" + name + "\", \"description\": \"" + description + "\"" + extra + " }";

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
    public async Task GetAll_ShouldShowTheManifestAndPersistTheProjection_WhenTheRepositoryCarriesOne()
    {
        var commit = Fixture.ProductManifests.Push(
            ForeverPin,
            Manifest("Forever Pin", "QR codes that outlive the print.", ", \"homepage\": \"https://foreverpin.example\", " +
                "\"services\": { \"redirect\": { \"name\": \"Redirect\" }, \"management\": { \"name\": \"Editor\", \"description\": \"Edits codes.\" } }"));

        var products = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();

        var foreverPin = products.Single(product => product.Slug == "foreverpin");
        (foreverPin.Name, foreverPin.Description, foreverPin.Homepage).Should().Be(
            ("Forever Pin", "QR codes that outlive the print.", "https://foreverpin.example"));
        foreverPin.Identity!.Source.Should().Be("manifest");
        foreverPin.Identity.Commit.Should().Be(commit);
        foreverPin.Identity.Problem.Should().BeNull();
        foreverPin.Identity.Services.Should().Equal(
            new ProductServiceResponse("management", "Editor", "Edits codes."),
            new ProductServiceResponse("redirect", "Redirect", null));

        // The other product's repository carries none, so it reads as its own row.
        var wheelhouse = products.Single(product => product.Slug == "wheelhouse");
        (wheelhouse.Name, wheelhouse.Homepage, wheelhouse.Identity!.Source, wheelhouse.Identity.Commit).Should().Be(
            ("Wheelhouse", null, "record", null));

        var row = (await RowsAsync()).Should().ContainSingle().Which;
        (row.Repository, row.Branch, row.Commit, row.Name, row.Homepage).Should().Be(
            (ForeverPin, "main", commit, "Forever Pin", "https://foreverpin.example"));
        row.Services.Select(service => service.Service).Should().Equal("management", "redirect");
    }

    [Fact]
    public async Task GetBySlug_ShouldReadTheManifestOnce_WhenTheHeadHasNotMoved()
    {
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));

        await GetAsync("foreverpin");
        await GetAsync("foreverpin");
        await GetAsync("foreverpin");

        Fixture.ProductManifests.Reads.Should().ContainSingle();
        Fixture.ProductManifests.HeadChecks.Should().HaveCount(3).And.OnlyContain(check => !check.Fresh);
    }

    [Fact]
    public async Task GetBySlug_ShouldReplaceTheRowWithTheNewerCommit_WhenTheHeadMoves()
    {
        var first = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        (await GetAsync("foreverpin")).Name.Should().Be("Forever Pin");

        var second = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin Plus", "Now with more."));
        var product = await GetAsync("foreverpin");

        (product.Name, product.Description, product.Identity!.Commit).Should().Be(("Forever Pin Plus", "Now with more.", second));
        var row = (await RowsAsync()).Should().ContainSingle().Which;
        (row.Commit, row.Name).Should().Be((second, "Forever Pin Plus"));
        row.Commit.Should().NotBe(first);
    }

    [Fact]
    public async Task GetBySlug_ShouldAdvanceTheCommit_WhenOnlyOtherFilesChanged()
    {
        var manifest = Manifest("Forever Pin");
        Fixture.ProductManifests.Push(ForeverPin, manifest);
        await GetAsync("foreverpin");

        var second = Fixture.ProductManifests.Push(ForeverPin, manifest);
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Commit).Should().Be(("Forever Pin", second));
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(second);
    }

    [Fact]
    public async Task GetBySlug_ShouldKeepThePreviousProjectionAndReportWhy_WhenTheNewestManifestIsInvalid()
    {
        var valid = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        await GetAsync("foreverpin");

        Fixture.ProductManifests.Push(ForeverPin, Manifest(new string('n', 81)));
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Commit).Should().Be(("Forever Pin", "manifest", valid));
        product.Identity.Problem.Should().Be("'name' must hold 1–80 characters.");
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(valid);

        // A later valid commit lifts the reason.
        var fixedCommit = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin Again"));
        var repaired = await GetAsync("foreverpin");
        (repaired.Name, repaired.Identity!.Commit, repaired.Identity.Problem).Should().Be(("Forever Pin Again", fixedCommit, null));
    }

    [Theory]
    [InlineData("{ nope", "The file is not valid JSON (line 1).")]
    [InlineData("[]", "The file must hold one JSON object.")]
    [InlineData("{ \"manifest\": 1, \"name\": \"Pilot\", \"description\": \"x\", \"homepage\": \"javascript:alert(1)\" }",
        "'homepage' must be an http or https address without credentials, at most 300 characters.")]
    [InlineData("{ \"manifest\": 1, \"name\": 5, \"description\": \"x\" }", "'name' must be text.")]
    public async Task GetBySlug_ShouldFallBackToTheRecordAndReportWhy_WhenTheFirstManifestIsInvalid(string file, string reason)
    {
        Fixture.ProductManifests.Push(ForeverPin, file);

        var product = await GetAsync("foreverpin");

        (product.Name, product.Description, product.Homepage).Should().Be(("ForeverPin", "Styled QR codes and short links.", null));
        (product.Identity!.Source, product.Identity.Commit, product.Identity.Problem).Should().Be(("record", null, reason));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySlug_ShouldReportTheSizeLimit_WhenTheManifestIsTooLarge()
    {
        Fixture.ProductManifests.Push(ForeverPin, ProductManifestFileStatus.TooLarge);

        var product = await GetAsync("foreverpin");

        product.Identity!.Problem.Should().Be("The file is larger than 16 KiB.");
        product.Identity.Source.Should().Be("record");
    }

    [Fact]
    public async Task GetBySlug_ShouldDropTheProjection_WhenTheNewestCommitHasNoManifest()
    {
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        (await GetAsync("foreverpin")).Identity!.Source.Should().Be("manifest");
        (await RowsAsync()).Should().HaveCount(1);

        Fixture.ProductManifests.Push(ForeverPin, ProductManifestFileStatus.Missing);
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Problem).Should().Be(("ForeverPin", "record", null));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySlug_ShouldKeepTheProjectionWithoutAReason_WhenTheRepositoryCannotBeRead()
    {
        var commit = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        await GetAsync("foreverpin");

        Fixture.ProductManifests.Unreachable(ForeverPin);
        var product = await GetAsync("foreverpin");

        (product.Name, product.Identity!.Source, product.Identity.Commit, product.Identity.Problem).Should().Be(
            ("Forever Pin", "manifest", commit, null));
        (await RowsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetBySlug_ShouldKeepTheProjection_WhenTheFileCannotBeRead()
    {
        var commit = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        await GetAsync("foreverpin");

        Fixture.ProductManifests.Push(ForeverPin, ProductManifestFileStatus.Unreadable);
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
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));

        await AdminClient.GetAsync("api/products");

        var audit = await (await AdminClient.GetAsync("api/audit")).Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(audit);
        document.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldAskForAFreshHeadAndReturnTheProduct_WhenTheOperatorRefreshes()
    {
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        await GetAsync("foreverpin");
        var second = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin Plus"));

        var response = await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Identity!.Commit).Should().Be(("Forever Pin Plus", second));
        Fixture.ProductManifests.HeadChecks.Last().Fresh.Should().BeTrue();
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(second);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturnTheReason_WhenTheNewestManifestIsInvalid()
    {
        var valid = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        await GetAsync("foreverpin");
        Fixture.ProductManifests.Push(ForeverPin, "{ \"manifest\": 1, \"name\": \"Pin\" }");

        var response = await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.ReadEnvelopeAsync<ProductResponse>();
        (product.Name, product.Identity!.Commit, product.Identity.Problem).Should().Be(("Forever Pin", valid, "'description' is required."));
    }

    [Fact]
    public async Task RefreshIdentity_ShouldFailAndKeepTheProjection_WhenTheBranchCannotBeRead()
    {
        var commit = Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
        await GetAsync("foreverpin");
        Fixture.ProductManifests.Unreachable(ForeverPin);

        var response = await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.ReadProblemAsync()).Detail.Should().Contain("could not be read from GitHub");
        (await RowsAsync()).Should().ContainSingle().Which.Commit.Should().Be(commit);
    }

    [Fact]
    public async Task RefreshIdentity_ShouldBeAudited_WhenTheOperatorRefreshes()
    {
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));

        await ActionClient("refresh").PostAsync("api/products/foreverpin/identity/refresh", null);

        var audit = await (await AdminClient.GetAsync("api/audit")).Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(audit);
        var entry = document.RootElement.GetProperty("data").EnumerateArray().Should().ContainSingle().Which;
        (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString())
            .Should().Be(("product.refresh", "foreverpin", "succeeded"));
    }

    [Fact]
    public async Task RefreshIdentity_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PostAsync("api/products/foreverpin/identity/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fixture.ProductManifests.HeadChecks.Should().BeEmpty();
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
    public async Task Update_ShouldLeaveTheRowsNameAlone_WhenAManifestGoverns()
    {
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin", "From the repository."));

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
    public async Task Update_ShouldSaveTheName_WhenNoManifestGoverns()
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
        Fixture.ProductManifests.Push(ForeverPin, Manifest("Forever Pin"));
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
    public async Task Update_ShouldReadTheNewRepositoryManifest_WhenTheRepositoryChanges()
    {
        Fixture.ProductManifests.Push("sulton-max/moved-repo", Manifest("Moved Pin"));

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
    public async Task Create_ShouldShowTheManifest_WhenTheNewProductsRepositoryCarriesOne()
    {
        Fixture.ProductManifests.Push("owner/newcomer", Manifest("Newcomer", "Fresh from its repository.", ", \"homepage\": \"https://newcomer.example\""));

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
        Fixture.ProductManifests.Push(Wheelhouse, Manifest("Wheelhouse"));
        await GetAsync("wheelhouse");
        (await RowsAsync()).Should().HaveCount(1);

        var response = await ActionClient("product").DeleteAsync("api/products/wheelhouse");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_ShouldGiveEveryProductItsOwnIdentity_WhenOnlyOneRepositoryCarriesAManifest()
    {
        Fixture.ProductManifests.Push(Wheelhouse, Manifest("Wheelhouse Console", "The deploy console."));
        Fixture.ProductManifests.Push(ForeverPin, "{ nope");

        var products = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();

        products.Select(product => (product.Slug, product.Name, product.Identity!.Source)).Should().Equal(
            ("foreverpin", "ForeverPin", "record"), ("wheelhouse", "Wheelhouse Console", "manifest"));
        products[0].Identity!.Problem.Should().NotBeNull();
        products[1].Identity!.Problem.Should().BeNull();
    }
}
