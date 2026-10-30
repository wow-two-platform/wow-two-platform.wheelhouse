using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the product catalog — identity from the code-owned catalog (the runner stub answers it), status from the
/// owner's records, and a projection with no targets, releases or deployment state.
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
    public async Task GetAll_ShouldReturn200WithEachProductsEnvironments_WhenTheOwnerReads()
    {
        var response = await AdminClient.GetAsync("api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await response.ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();
        products.Select(product => product.Slug).Should().Equal("foreverpin", "wheelhouse");

        var foreverPin = products[0];
        foreverPin.Status.Should().Be("active");
        foreverPin.InactiveServices.Should().BeEmpty();
        foreverPin.Repository.Should().Be(new ProductRepositoryResponse(
            "sulton-max/10x-venture-forever-pin", "https://github.com/sulton-max/10x-venture-forever-pin", "main"));
        foreverPin.IconUrl.Should().Be("/api/products/foreverpin/icon");
        foreverPin.Environments.Select(environment => environment.Name).Should().Equal("dev", "prod");
        foreverPin.Environments[0].Sites.Should().Equal(new ProductSiteResponse("app", "https://dev.foreverpin.example", "public"));
        foreverPin.Environments[0].Secrets.Should().Be(new ProductSecretsResponse("pilot-vault", "foreverpin-dev"));
        foreverPin.Environments[1].Secrets.Should().Be(new ProductSecretsResponse("pilot-vault", "foreverpin-prod"),
            "a server without a vault keeps its environments' secrets in the inventory's first");
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
    public async Task UpdateStatus_ShouldReturn200AndPersist_WhenTheOwnerMarksAProductInactive()
    {
        var response = await ActionClient("status").PutJsonAsync("api/products/foreverpin/status", new { status = "inactive" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<ProductResponse>()).Status.Should().Be("inactive");
        var reread = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();
        reread.Select(product => (product.Slug, product.Status)).Should().Equal(("foreverpin", "inactive"), ("wheelhouse", "active"));

        (await ActionClient("status").PutJsonAsync("api/products/foreverpin/status", new { status = "active" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProductAsync("foreverpin")).Status.Should().Be("active");
    }

    [Fact]
    public async Task UpdateStatus_ShouldAcceptTheProductsId_WhenTheRouteNamesItThatWay()
    {
        var id = (await ProductAsync("wheelhouse")).Id;

        var response = await ActionClient("status").PutJsonAsync($"api/products/{id}/status", new { status = "inactive" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProductAsync("wheelhouse")).Status.Should().Be("inactive");
    }

    [Fact]
    public async Task UpdateStatus_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PutJsonAsync("api/products/foreverpin/status", new { status = "inactive" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateStatus_ShouldReturn404_WhenTheCatalogLacksTheProduct()
    {
        var response = await ActionClient("status").PutJsonAsync("api/products/ghost/status", new { status = "inactive" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("paused")]
    [InlineData("2")]
    public async Task UpdateStatus_ShouldReturn400_WhenTheStatusIsOutsideTheSet(string status)
    {
        var response = await ActionClient("status").PutJsonAsync("api/products/foreverpin/status", new { status });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateServiceStatus_ShouldListTheServiceAsInactive_UntilItIsActiveAgain()
    {
        var client = ActionClient("status");

        var off = await client.PutJsonAsync("api/products/foreverpin/services/redirect/status", new { status = "inactive" });
        (await client.PutJsonAsync("api/products/foreverpin/services/management/status", new { status = "inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        off.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProductAsync("foreverpin")).InactiveServices.Should().Equal("management", "redirect");
        (await ProductAsync("foreverpin")).Status.Should().Be("active", "a service's status leaves its product's alone");
        (await ProductAsync("wheelhouse")).InactiveServices.Should().BeEmpty();

        var on = await client.PutJsonAsync("api/products/foreverpin/services/management/status", new { status = "active" });
        (await on.ReadEnvelopeAsync<ProductResponse>()).InactiveServices.Should().Equal("redirect");
    }

    [Fact]
    public async Task UpdateServiceStatus_ShouldReturn404Or400_WhenTheProductOrServiceIsUnknownOrMalformed()
    {
        var client = ActionClient("status");

        (await client.PutJsonAsync("api/products/ghost/services/api/status", new { status = "inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutJsonAsync("api/products/foreverpin/services/Not_A_Key/status", new { status = "inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.PutJsonAsync("api/products/foreverpin/services/api/status", new { status = "inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InactiveProduct_ShouldStillReadAndDeploy_SinceInactiveOnlyHidesIt()
    {
        var client = ActionClient("status");
        (await client.PutJsonAsync("api/products/foreverpin/status", new { status = "inactive" })).EnsureSuccessStatusCode();
        (await client.PutJsonAsync("api/products/foreverpin/services/management/status", new { status = "inactive" })).EnsureSuccessStatusCode();

        (await AdminClient.GetAsync("api/products/foreverpin")).StatusCode.Should().Be(HttpStatusCode.OK);
        var deploy = AdminClient;
        deploy.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "deploy");
        (await deploy.PostJsonAsync("api/deployments", new { target = "foreverpin-dev", release = "v1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task StatusWrites_ShouldLandInTheAuditTrail()
    {
        var client = ActionClient("status");
        (await client.PutJsonAsync("api/products/foreverpin/status", new { status = "inactive" })).EnsureSuccessStatusCode();
        (await client.PutJsonAsync("api/products/foreverpin/services/redirect/status", new { status = "inactive" })).EnsureSuccessStatusCode();

        var entries = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Select(entry => (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(), entry.GetProperty("detail").GetString()))
            .Reverse();
        entries.Should().Equal(("product.status", "foreverpin", "Inactive"), ("service.status", "foreverpin/redirect", "Inactive"));
    }

    [Fact]
    public async Task DeleteProduct_ShouldTakeItsServiceStatusesWithIt()
    {
        var client = ActionClient("status");
        (await client.PutJsonAsync("api/products/wheelhouse/services/console/status", new { status = "inactive" })).EnsureSuccessStatusCode();

        (await ActionClient("product").DeleteAsync("api/products/wheelhouse")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AdminClient.GetAsync("api/products/wheelhouse")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<ProductResponse> ProductAsync(string slug) =>
        await (await AdminClient.GetAsync("api/products/" + slug)).ReadEnvelopeAsync<ProductResponse>();
}
