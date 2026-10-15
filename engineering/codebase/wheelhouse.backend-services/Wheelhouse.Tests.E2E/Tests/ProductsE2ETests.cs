using System.Net;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Backend.Beta.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the product catalog — identity from the code-owned catalog (the runner stub answers it), lifecycle from the
/// operator's records, and a projection with no targets, releases or deployment state.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ProductsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    [Fact]
    public async Task GetAll_ShouldReturn200WithEachProductsEnvironments_WhenTheOperatorReads()
    {
        var response = await AdminClient.GetAsync("api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await response.ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();
        products.Select(product => product.Slug).Should().Equal("foreverpin", "wheelhouse");

        var foreverPin = products[0];
        foreverPin.Lifecycle.Should().Be("building");
        foreverPin.Repository.Should().Be(new ProductRepositoryResponse(
            "sulton-max/10x-venture-forever-pin", "https://github.com/sulton-max/10x-venture-forever-pin", "main"));
        foreverPin.IconUrl.Should().Be("/api/products/foreverpin/icon");
        foreverPin.Environments.Select(environment => environment.Name).Should().Equal("dev", "prod");
        foreverPin.Environments[0].Sites.Should().Equal(new ProductSiteResponse("app", "https://dev.foreverpin.example", "public"));
        foreverPin.Environments[0].Secrets.Should().Be(new ProductSecretsResponse("pilot-vault", "foreverpin-dev"));
        foreverPin.Environments[1].Secrets.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_ShouldLeaveDeploymentStateOut_WhenTheCatalogIsProjected()
    {
        var body = await AdminClient.GetStringAsync("api/products");

        body.Should().NotContain("targetId").And.NotContain("serverId").And.NotContain("hasReleaseSource");
    }

    [Fact]
    public async Task GetBySlug_ShouldReturn200_WhenTheCatalogDefinesTheProduct()
    {
        var response = await AdminClient.GetAsync("api/products/wheelhouse");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<ProductResponse>()).Name.Should().Be("Wheelhouse");
    }

    [Fact]
    public async Task GetBySlug_ShouldReturn404_WhenTheCatalogLacksTheProduct()
    {
        var response = await AdminClient.GetAsync("api/products/ghost");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var problem = await response.ReadProblemAsync();
        problem.Status.Should().Be(404);
        problem.Detail.Should().NotBeNullOrWhiteSpace();
        problem.Extensions["code"]?.ToString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetBySlug_ShouldReturn400_WhenTheSlugIsNotACatalogSlug()
    {
        var response = await AdminClient.GetAsync("api/products/ForeverPin");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateLifecycle_ShouldReturn200AndPersist_WhenTheOperatorRecordsIt()
    {
        var response = await ActionClient("lifecycle").PutJsonAsync("api/products/foreverpin/lifecycle", new { lifecycle = "live" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<ProductResponse>()).Lifecycle.Should().Be("live");
        var reread = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();
        reread.Select(product => (product.Slug, product.Lifecycle)).Should().Equal(("foreverpin", "live"), ("wheelhouse", "building"));
    }

    [Fact]
    public async Task UpdateLifecycle_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PutJsonAsync("api/products/foreverpin/lifecycle", new { lifecycle = "live" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateLifecycle_ShouldReturn404_WhenTheCatalogLacksTheProduct()
    {
        var response = await ActionClient("lifecycle").PutJsonAsync("api/products/ghost/lifecycle", new { lifecycle = "live" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("2")]
    public async Task UpdateLifecycle_ShouldReturn400_WhenTheLifecycleIsOutsideTheSet(string lifecycle)
    {
        var response = await ActionClient("lifecycle").PutJsonAsync("api/products/foreverpin/lifecycle", new { lifecycle });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
