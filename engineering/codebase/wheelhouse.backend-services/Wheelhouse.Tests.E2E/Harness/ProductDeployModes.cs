using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Sets what deploys a product as the product form does: its definition saved again with another
/// <c>deploy</c>, everything else as it stands.</summary>
public static class ProductDeployModes
{
    /// <summary>Saves the product with <paramref name="deploy"/> through <c>PUT /api/products/{slug}</c>.</summary>
    /// <param name="admin">A signed-in owner's client.</param>
    /// <param name="product">The product's slug.</param>
    /// <param name="deploy"><c>wheelhouse</c>, <c>ci</c> or <c>none</c>.</param>
    /// <returns>The saved product.</returns>
    public static async Task<JsonElement> SetAsync(HttpClient admin, string product, string deploy)
    {
        var current = (await admin.GetFromJsonAsync<JsonElement>("/api/products/" + product)).GetProperty("data");
        var repository = current.GetProperty("repository");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/products/" + product)
        {
            Content = JsonContent.Create(new
            {
                name = current.GetProperty("name").GetString(),
                description = current.GetProperty("description").GetString(),
                repository = repository.GetProperty("name").GetString(),
                defaultBranch = repository.GetProperty("defaultBranch").GetString(),
                release = current.GetProperty("release"),
                deploy,
            }),
            Headers = { { "X-Wheelhouse-Action", "product" } },
        };
        var response = await admin.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
