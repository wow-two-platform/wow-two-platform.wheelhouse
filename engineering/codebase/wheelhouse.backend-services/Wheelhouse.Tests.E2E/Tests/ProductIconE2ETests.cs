using System.Net;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for product icons: the icon convention's file for the requested theme, form and color, a service's own icon
/// before the product's, the fallbacks behind a missing variant, and not found so the console shows a monogram.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ProductIconE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string ForeverPin = "sulton-max/10x-venture-forever-pin";
    private const string Wheelhouse = "wow-two-platform/wow-two-platform.wheelhouse";
    private const string Icons = "product/brand/icons/";

    private async Task<string?> ServedAsync(string url)
    {
        var response = await AdminClient.GetAsync(url);
        return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : null;
    }

    [Fact]
    public async Task GetIcon_ShouldReturn200AsADownload_WhenTheRepositoryCarriesAnIcon()
    {
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-light.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        var response = await AdminClient.GetAsync("api/products/foreverpin/icon");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        (await response.Content.ReadAsStringAsync()).Should().Be("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        Fixture.ProductIcons.Listings.Should().Equal((ForeverPin, "main"));
    }

    [Fact]
    public async Task GetIcon_ShouldServeTheVariantOfTheRequestedTheme_WhenBothThemesExist()
    {
        // Wheelhouse's own icon set: the light boat on light surfaces, the dark boat on dark ones.
        Fixture.ProductIcons.Add(Wheelhouse, Icons + "short-colored-light.png", "light-boat");
        Fixture.ProductIcons.Add(Wheelhouse, Icons + "short-colored-dark.png", "dark-boat");

        (await ServedAsync("api/products/wheelhouse/icon")).Should().Be("light-boat");
        (await ServedAsync("api/products/wheelhouse/icon?theme=light")).Should().Be("light-boat");
        (await ServedAsync("api/products/wheelhouse/icon?theme=dark")).Should().Be("dark-boat");
        (await ServedAsync("api/products/wheelhouse/icon?theme=Dark")).Should().Be("dark-boat");
    }

    [Fact]
    public async Task GetIcon_ShouldServeTheRequestedFormAndColor_WhenTheVariantExists()
    {
        foreach (var form in new[] { "full", "short" })
            foreach (var color in new[] { "colored", "monochrome" })
                foreach (var theme in new[] { "light", "dark" })
                    Fixture.ProductIcons.Add(ForeverPin, $"{Icons}{form}-{color}-{theme}.svg", $"{form}-{color}-{theme}");

        (await ServedAsync("api/products/foreverpin/icon?form=full&color=monochrome&theme=dark")).Should().Be("full-monochrome-dark");
        (await ServedAsync("api/products/foreverpin/icon?form=short&theme=light")).Should().Be("short-colored-light");
        (await ServedAsync("api/products/foreverpin/icon?color=monochrome")).Should().Be("short-monochrome-light");
    }

    [Fact]
    public async Task GetIcon_ShouldFallBackThroughColorFormAndTheme_WhenTheVariantIsMissing()
    {
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-monochrome-dark.png", "mono-dark");
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-light.png", "colored-light");

        // The other color of the requested theme beats the requested color of the other theme.
        (await ServedAsync("api/products/foreverpin/icon?theme=dark")).Should().Be("mono-dark");
        // A full request falls to the short symbol of the same theme.
        (await ServedAsync("api/products/foreverpin/icon?theme=dark&form=full")).Should().Be("mono-dark");
        (await ServedAsync("api/products/foreverpin/icon?theme=light&form=full&color=monochrome")).Should().Be("colored-light");
    }

    [Fact]
    public async Task GetIcon_ShouldServeTheOtherTheme_WhenTheRequestedOneHasNone()
    {
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-light.png", "colored-light");

        (await ServedAsync("api/products/foreverpin/icon?theme=dark")).Should().Be("colored-light");
    }

    [Fact]
    public async Task GetIcon_ShouldServeTheWebAppFavicon_WhenTheRepositoryHasNoIconSet()
    {
        const string favicons = "engineering/codebase/forever-pin.frontend-services/apps/web/public/";
        Fixture.ProductIcons.Add(ForeverPin, favicons + "favicon.svg", "favicon");
        Fixture.ProductIcons.Add(ForeverPin, favicons + "favicon-dark.svg", "favicon-dark");

        (await ServedAsync("api/products/foreverpin/icon")).Should().Be("favicon");
        (await ServedAsync("api/products/foreverpin/icon?theme=dark")).Should().Be("favicon-dark");

        // The icon set, once present, outranks the favicon — even from the other theme.
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-light.svg", "colored-light");
        (await ServedAsync("api/products/foreverpin/icon?theme=dark")).Should().Be("colored-light");
    }

    [Fact]
    public async Task GetServiceIcon_ShouldServeTheServicesOwnIconThenTheProducts_WhenAskedPerService()
    {
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-light.svg", "product-light");
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-dark.svg", "product-dark");
        Fixture.ProductIcons.Add(ForeverPin, Icons + "services/redirect/short-colored-light.svg", "redirect-light");
        Fixture.ProductIcons.Add(ForeverPin, Icons + "services/redirect/short-colored-dark.svg", "redirect-dark");

        (await ServedAsync("api/products/foreverpin/services/redirect/icon")).Should().Be("redirect-light");
        (await ServedAsync("api/products/foreverpin/services/redirect/icon?theme=dark")).Should().Be("redirect-dark");
        // A service with no icons of its own shows the product's, in the requested theme.
        (await ServedAsync("api/products/foreverpin/services/management/icon?theme=dark")).Should().Be("product-dark");
        // The product's own icon never comes from a service folder.
        (await ServedAsync("api/products/foreverpin/icon?theme=dark")).Should().Be("product-dark");
    }

    [Fact]
    public async Task GetServiceIcon_ShouldPreferTheRightThemeToTheRightScope_WhenTheServiceHasOneTheme()
    {
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-dark.svg", "product-dark");
        Fixture.ProductIcons.Add(ForeverPin, Icons + "services/redirect/short-colored-light.svg", "redirect-light");

        (await ServedAsync("api/products/foreverpin/services/redirect/icon?theme=dark")).Should().Be("product-dark");
        (await ServedAsync("api/products/foreverpin/services/redirect/icon?theme=light")).Should().Be("redirect-light");
    }

    [Fact]
    public async Task GetIcon_ShouldReturn404_WhenTheRepositoryCarriesNone()
    {
        (await AdminClient.GetAsync("api/products/wheelhouse/icon")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AdminClient.GetAsync("api/products/wheelhouse/services/console/icon")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIcon_ShouldReturn404_WhenOnlyFilesOutsideTheConventionExist()
    {
        Fixture.ProductIcons.Add(ForeverPin, "docs/logo.png", "logo");
        Fixture.ProductIcons.Add(ForeverPin, "web/public/favicon.svg", "favicon");
        Fixture.ProductIcons.Add(ForeverPin, Icons + "mark.svg", "mark");

        (await AdminClient.GetAsync("api/products/foreverpin/icon")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIcon_ShouldReturn404_WhenTheCatalogLacksTheProduct()
    {
        (await AdminClient.GetAsync("api/products/ghost/icon")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AdminClient.GetAsync("api/products/ghost/services/api/icon")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("api/products/foreverpin/icon?theme=sepia")]
    [InlineData("api/products/foreverpin/icon?theme=7")]
    [InlineData("api/products/foreverpin/icon?form=wide")]
    [InlineData("api/products/foreverpin/icon?color=duotone")]
    [InlineData("api/products/foreverpin/services/Redirect/icon")]
    [InlineData("api/products/foreverpin/services/..%2Fsecrets/icon")]
    public async Task GetIcon_ShouldReturn400_WhenAVariantAxisOrTheServiceIsMalformed(string url)
    {
        Fixture.ProductIcons.Add(ForeverPin, Icons + "short-colored-light.svg", "light");

        (await AdminClient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("api/products/foreverpin/icon")]
    [InlineData("api/products/foreverpin/services/redirect/icon?theme=dark")]
    public async Task GetIcon_ShouldReturn401_WhenTheCallerIsAnonymous(string url)
    {
        (await AnonymousClient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
