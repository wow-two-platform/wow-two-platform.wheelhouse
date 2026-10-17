using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for workspaces — the operator groups products into them, orders them and moves products between them; every
/// product belongs to exactly one, a workspace holding products stays, and every write lands in the audit trail.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class WorkspacesE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<IReadOnlyList<WorkspaceResponse>> ListAsync() =>
        await (await AdminClient.GetAsync("api/workspaces")).ReadEnvelopeAsync<IReadOnlyList<WorkspaceResponse>>();

    private async Task<WorkspaceResponse> CreateAsync(string slug, string name, string? description = null)
    {
        var response = await ActionClient("workspace").PostJsonAsync("api/workspaces", new { slug, name, description });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.ReadEnvelopeAsync<WorkspaceResponse>();
    }

    private async Task<ProductResponse> ProductAsync(string slug) =>
        await (await AdminClient.GetAsync("api/products/" + slug)).ReadEnvelopeAsync<ProductResponse>();

    private async Task<JsonElement[]> AuditAsync()
    {
        var document = await AdminClient.GetFromJsonAsync<JsonElement>("api/audit");
        return [.. document.GetProperty("data").EnumerateArray()];
    }

    [Fact]
    public async Task GetAll_ShouldListTheDefaultWorkspaceHoldingEveryProduct_WhenNothingElseExists()
    {
        var workspaces = await ListAsync();

        var only = workspaces.Should().ContainSingle().Subject;
        (only.Slug, only.Name, only.Description, only.Order).Should().Be(("own-ventures", "Own ventures", (string?)null, 0));
        var products = await (await AdminClient.GetAsync("api/products")).ReadEnvelopeAsync<IReadOnlyList<ProductResponse>>();
        products.Should().OnlyContain(product => product.WorkspaceId == only.Id);
        products.Should().OnlyContain(product => product.Id != Guid.Empty);
    }

    [Fact]
    public async Task Create_ShouldReturn201AndComeLast_WhenTheOperatorAddsWorkspaces()
    {
        var platform = await CreateAsync("wow2-platform", "wow2 platform", "  The platform apps.  ");
        var personal = await CreateAsync("personal", "Personal");

        (platform.Slug, platform.Name, platform.Description, platform.Order)
            .Should().Be(("wow2-platform", "wow2 platform", "The platform apps.", 1));
        personal.Description.Should().BeNull();
        (await ListAsync()).Select(workspace => workspace.Slug).Should().Equal("own-ventures", "wow2-platform", "personal");
    }

    [Fact]
    public async Task Create_ShouldReturn409_WhenTheSlugIsTaken()
    {
        var response = await ActionClient("workspace").PostJsonAsync("api/workspaces", new { slug = "own-ventures", name = "Again" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Contain("own-ventures");
    }

    [Theory]
    [InlineData("Own-Ventures", "Name")]
    [InlineData("9lives", "Name")]
    [InlineData("valid", "")]
    public async Task Create_ShouldReturn400_WhenTheSlugOrNameIsInvalid(string slug, string name)
    {
        var response = await ActionClient("workspace").PostJsonAsync("api/workspaces", new { slug, name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Writes_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var id = (await ListAsync())[0].Id;

        (await AdminClient.PostJsonAsync("api/workspaces", new { slug = "x", name = "X" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.PutJsonAsync("api/workspaces/" + id, new { name = "X" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.DeleteAsync("api/workspaces/" + id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.PutJsonAsync("api/workspaces/order", new { ids = new[] { id } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Endpoints_ShouldReturn401_WhenTheCallerIsAnonymous()
    {
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "workspace");

        (await client.GetAsync("api/workspaces")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostJsonAsync("api/workspaces", new { slug = "x", name = "X" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutJsonAsync("api/products/foreverpin/workspace", new { workspaceId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CatalogKey_ShouldReadWorkspacesButNotChangeThem()
    {
        var created = await ActionClient("key-create").PostJsonAsync("api/integration-keys", new { name = "Reader", scopes = new[] { "catalog:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "workspace");

        (await client.GetAsync("api/workspaces")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostJsonAsync("api/workspaces", new { slug = "x", name = "X" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_ShouldRenameAndRedescribe_WhenTheWorkspaceExists()
    {
        var workspace = await CreateAsync("wow2-apps", "Apps", "Old.");

        var response = await ActionClient("workspace").PutJsonAsync("api/workspaces/" + workspace.Id, new { name = "wow2 apps", description = " " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.ReadEnvelopeAsync<WorkspaceResponse>();
        (updated.Id, updated.Slug, updated.Name, updated.Description, updated.Order)
            .Should().Be((workspace.Id, "wow2-apps", "wow2 apps", (string?)null, workspace.Order));
        (await ListAsync()).Single(item => item.Id == workspace.Id).Name.Should().Be("wow2 apps");
    }

    [Fact]
    public async Task UpdateAndDelete_ShouldReturn404_WhenTheWorkspaceIsUnknown()
    {
        var unknown = Guid.NewGuid();

        (await ActionClient("workspace").PutJsonAsync("api/workspaces/" + unknown, new { name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ActionClient("workspace").DeleteAsync("api/workspaces/" + unknown)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ShouldReturn204_WhenTheWorkspaceHoldsNoProduct()
    {
        var empty = await CreateAsync("other-ventures", "Other ventures");

        var response = await ActionClient("workspace").DeleteAsync("api/workspaces/" + empty.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync()).Select(workspace => workspace.Slug).Should().Equal("own-ventures");
    }

    [Fact]
    public async Task Delete_ShouldReturn409Problem_WhileProductsRemain()
    {
        var holding = (await ListAsync())[0];

        var response = await ActionClient("workspace").DeleteAsync("api/workspaces/" + holding.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.ReadProblemAsync()).Detail.Should().Contain("still holds products");
        (await ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Reorder_ShouldReturnTheNewOrder_WhenEveryWorkspaceIsNamedOnce()
    {
        var own = (await ListAsync())[0];
        var platform = await CreateAsync("wow2-platform", "wow2 platform");
        var personal = await CreateAsync("personal", "Personal");

        var response = await ActionClient("workspace").PutJsonAsync("api/workspaces/order", new { ids = new[] { personal.Id, own.Id, platform.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ordered = await response.ReadEnvelopeAsync<IReadOnlyList<WorkspaceResponse>>();
        ordered.Select(workspace => (workspace.Slug, workspace.Order)).Should().Equal(("personal", 0), ("own-ventures", 1), ("wow2-platform", 2));
        (await ListAsync()).Select(workspace => workspace.Slug).Should().Equal("personal", "own-ventures", "wow2-platform");
    }

    [Fact]
    public async Task Reorder_ShouldReturn409AndKeepTheOrder_WhenAWorkspaceIsMissingOrUnknown()
    {
        var own = (await ListAsync())[0];
        var platform = await CreateAsync("wow2-platform", "wow2 platform");

        (await ActionClient("workspace").PutJsonAsync("api/workspaces/order", new { ids = new[] { platform.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ActionClient("workspace").PutJsonAsync("api/workspaces/order", new { ids = new[] { platform.Id, own.Id, Guid.NewGuid() } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ListAsync()).Select(workspace => workspace.Slug).Should().Equal("own-ventures", "wow2-platform");
    }

    [Fact]
    public async Task Reorder_ShouldReturn400_WhenAnIdentifierRepeats()
    {
        var own = (await ListAsync())[0];

        var response = await ActionClient("workspace").PutJsonAsync("api/workspaces/order", new { ids = new[] { own.Id, own.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MoveProduct_ShouldPutItInTheWorkspace_WhenTheRouteNamesItByIdOrSlug(bool byId)
    {
        var platform = await CreateAsync("wow2-platform", "wow2 platform");
        var wheelhouse = await ProductAsync("wheelhouse");

        var response = await ActionClient("workspace").PutJsonAsync(
            "api/products/" + (byId ? wheelhouse.Id.ToString() : "wheelhouse") + "/workspace", new { workspaceId = platform.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<ProductResponse>()).WorkspaceId.Should().Be(platform.Id);
        (await ProductAsync("wheelhouse")).WorkspaceId.Should().Be(platform.Id);
        (await ProductAsync("foreverpin")).WorkspaceId.Should().NotBe(platform.Id);
    }

    [Fact]
    public async Task MoveProduct_ShouldReturn400_WhenTheWorkspaceIsUnknown()
    {
        var response = await ActionClient("workspace").PutJsonAsync("api/products/wheelhouse/workspace", new { workspaceId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("ghost")]
    [InlineData("0199e5a4-0000-7000-8000-000000000000")]
    public async Task MoveProduct_ShouldReturn404_WhenTheProductIsUnknown(string product)
    {
        var own = (await ListAsync())[0];

        var response = await ActionClient("workspace").PutJsonAsync("api/products/" + product + "/workspace", new { workspaceId = own.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MoveProduct_ShouldReturn400_WhenTheRouteIsNeitherAnIdNorASlug()
    {
        var own = (await ListAsync())[0];

        var response = await ActionClient("workspace").PutJsonAsync("api/products/Not_A_Slug/workspace", new { workspaceId = own.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_ShouldJoinTheNamedWorkspace_OrTheDefaultOne()
    {
        var platform = await CreateAsync("wow2-platform", "wow2 platform");
        var own = (await ListAsync()).Single(workspace => workspace.Slug == "own-ventures");

        var named = await ActionClient("product").PostJsonAsync("api/products",
            new { slug = "vault", name = "Vault", repository = "owner/vault", workspaceId = platform.Id });
        var unnamed = await ActionClient("product").PostJsonAsync("api/products",
            new { slug = "pilot", name = "Pilot", repository = "owner/pilot" });
        var unknown = await ActionClient("product").PostJsonAsync("api/products",
            new { slug = "stray", name = "Stray", repository = "owner/stray", workspaceId = Guid.NewGuid() });

        (await named.ReadEnvelopeAsync<ProductResponse>()).WorkspaceId.Should().Be(platform.Id);
        (await unnamed.ReadEnvelopeAsync<ProductResponse>()).WorkspaceId.Should().Be(own.Id);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.GetAsync("api/products/stray")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateProduct_ShouldBringTheDefaultWorkspaceBack_WhenItWasRemoved()
    {
        var platform = await CreateAsync("wow2-platform", "wow2 platform");
        foreach (var product in new[] { "foreverpin", "wheelhouse" })
            (await ActionClient("workspace").PutJsonAsync($"api/products/{product}/workspace", new { workspaceId = platform.Id }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        var own = (await ListAsync()).Single(workspace => workspace.Slug == "own-ventures");
        (await ActionClient("workspace").DeleteAsync("api/workspaces/" + own.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var created = await ActionClient("product").PostJsonAsync("api/products", new { slug = "pilot", name = "Pilot", repository = "owner/pilot" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var back = (await ListAsync()).Should().Contain(workspace => workspace.Slug == "own-ventures").Subject;
        back.Order.Should().Be(2);
        (await created.ReadEnvelopeAsync<ProductResponse>()).WorkspaceId.Should().Be(back.Id);
    }

    [Fact]
    public async Task Writes_ShouldLandInTheAuditTrail_RefusalsIncluded()
    {
        var workspace = await CreateAsync("personal", "Personal");
        (await ActionClient("workspace").PutJsonAsync("api/workspaces/" + workspace.Id, new { name = "Mine" })).EnsureSuccessStatusCode();
        (await ActionClient("workspace").PutJsonAsync("api/products/wheelhouse/workspace", new { workspaceId = workspace.Id })).EnsureSuccessStatusCode();
        var own = (await ListAsync()).Single(item => item.Slug == "own-ventures");
        (await ActionClient("workspace").PutJsonAsync("api/workspaces/order", new { ids = new[] { workspace.Id, own.Id } })).EnsureSuccessStatusCode();
        (await ActionClient("workspace").DeleteAsync("api/workspaces/" + workspace.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var entries = (await AuditAsync()).Reverse()
            .Select(entry => (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString()))
            .ToList();
        entries.Should().Equal(
            ("workspace.create", "personal", "succeeded"),
            ("workspace.update", workspace.Id.ToString(), "succeeded"),
            ("product.workspace", "wheelhouse", "succeeded"),
            ("workspace.reorder", "workspaces", "succeeded"),
            ("workspace.delete", workspace.Id.ToString(), "failed"));
    }
}
