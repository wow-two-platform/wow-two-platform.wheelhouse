using AwesomeAssertions;
using Wheelhouse.Application.Products;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Which repository file becomes a product's or service's icon: the icon convention's paths, theme and
/// variant selection, service-to-product fallback and the web app favicon behind them.</summary>
public sealed class ProductIconPathsTests
{
    private const string Favicons = "engineering/codebase/app.frontend-services/apps/web/public/";

    /// <summary>Expands a short name into a repository path: a convention icon under the icon folder, or a path that
    /// already starts at the repository root.</summary>
    private static string Repo(string name) =>
        name.StartsWith("engineering/", StringComparison.Ordinal) ? name : $"{ProductIconPaths.Folder}/{name}";

    private static string? Choose(string files, string theme, string form, string color, string? service) =>
        ProductIconPaths.Choose(
            files.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Repo),
            new ProductIconRequest
            {
                Service = service,
                Theme = Enum.Parse<IconTheme>(theme, ignoreCase: true),
                Form = Enum.Parse<IconForm>(form, ignoreCase: true),
                Color = Enum.Parse<IconColor>(color, ignoreCase: true),
            });

    [Theory]
    // The requested variant wins over every other.
    [InlineData("short-colored-light.svg full-colored-light.svg short-colored-dark.svg", "light", "short", "colored", null, "short-colored-light.svg")]
    [InlineData("short-colored-light.svg full-colored-light.svg short-colored-dark.svg", "light", "full", "colored", null, "full-colored-light.svg")]
    // svg beats png for the same variant.
    [InlineData("short-colored-light.png short-colored-light.svg", "light", "short", "colored", null, "short-colored-light.svg")]
    // Theme: the dark console gets the dark file, the light console the light one.
    [InlineData("short-colored-light.png short-colored-dark.png", "dark", "short", "colored", null, "short-colored-dark.png")]
    [InlineData("short-colored-light.png short-colored-dark.png", "light", "short", "colored", null, "short-colored-light.png")]
    // Color: the other color of the same theme beats the same color of the other theme.
    [InlineData("short-monochrome-dark.svg short-colored-light.svg", "dark", "short", "colored", null, "short-monochrome-dark.svg")]
    [InlineData("short-colored-light.svg short-colored-dark.svg", "dark", "short", "monochrome", null, "short-colored-dark.svg")]
    // Form: a full request falls to the short symbol, never the other way round.
    [InlineData("short-colored-dark.svg short-colored-light.svg", "dark", "full", "colored", null, "short-colored-dark.svg")]
    [InlineData("full-monochrome-dark.svg short-colored-dark.svg", "dark", "full", "colored", null, "full-monochrome-dark.svg")]
    [InlineData("short-monochrome-dark.svg short-colored-dark.svg", "dark", "full", "monochrome", null, "short-monochrome-dark.svg")]
    [InlineData("full-colored-dark.svg full-colored-light.svg", "dark", "short", "colored", null, null)]
    // The other theme is used only when the requested one has nothing.
    [InlineData("short-colored-light.png", "dark", "short", "colored", null, "short-colored-light.png")]
    [InlineData("full-colored-light.png short-colored-dark.png", "light", "short", "colored", null, "short-colored-dark.png")]
    // Services: their own folder first, the product's folder next, and the right theme before the right scope.
    [InlineData("short-colored-dark.svg services/redirect/short-colored-dark.svg", "dark", "short", "colored", "redirect", "services/redirect/short-colored-dark.svg")]
    [InlineData("short-colored-dark.svg services/redirect/short-colored-light.svg", "dark", "short", "colored", "redirect", "short-colored-dark.svg")]
    [InlineData("short-colored-dark.svg services/redirect/short-colored-dark.svg", "dark", "short", "colored", "management", "short-colored-dark.svg")]
    [InlineData("services/redirect/short-colored-light.svg", "dark", "short", "colored", "redirect", "services/redirect/short-colored-light.svg")]
    [InlineData("services/redirect/short-colored-dark.svg", "dark", "short", "colored", null, null)]
    // The web app's favicon follows the icon set: dark first on a dark theme, svg before png.
    [InlineData(Favicons + "favicon.png " + Favicons + "favicon-dark.png", "dark", "short", "colored", null, Favicons + "favicon-dark.png")]
    [InlineData(Favicons + "favicon.png " + Favicons + "favicon-dark.png", "light", "short", "colored", null, Favicons + "favicon.png")]
    [InlineData(Favicons + "favicon.svg " + Favicons + "favicon-dark.png", "dark", "short", "colored", null, Favicons + "favicon-dark.png")]
    [InlineData(Favicons + "favicon.png " + Favicons + "favicon.svg", "dark", "short", "colored", null, Favicons + "favicon.svg")]
    [InlineData(Favicons + "favicon-dark.png", "light", "short", "colored", null, null)]
    [InlineData("short-colored-light.svg " + Favicons + "favicon-dark.svg", "dark", "short", "colored", null, "short-colored-light.svg")]
    // Nothing resolves, so the caller shows its monogram.
    [InlineData("", "light", "short", "colored", null, null)]
    [InlineData("README.md src/main.ts images/hero.png favicon.svg", "light", "short", "colored", null, null)]
    public void Choose_FollowsTheIconConvention(string files, string theme, string form, string color, string? service, string? expected)
    {
        var chosen = Choose(files, theme, form, color, service);

        chosen.Should().Be(expected is null ? null : Repo(expected));
    }

    [Fact]
    public void Choose_ResolvesEveryVariantOfAFullIconSet()
    {
        var files = string.Join(' ', from form in new[] { "full", "short" }
                                     from color in new[] { "colored", "monochrome" }
                                     from theme in new[] { "light", "dark" }
                                     select $"{form}-{color}-{theme}.svg");

        foreach (var theme in new[] { "light", "dark" })
            foreach (var form in new[] { "full", "short" })
                foreach (var color in new[] { "colored", "monochrome" })
                    Choose(files, theme, form, color, null).Should().Be(Repo($"{form}-{color}-{theme}.svg"));
    }

    [Fact]
    public void Candidates_ListTheRequestedThemeBeforeTheOtherAndTheServiceBeforeTheProduct()
    {
        var candidates = ProductIconPaths.Candidates(new ProductIconRequest { Service = "redirect", Theme = IconTheme.Dark });

        candidates.Should().Equal(
            "product/brand/icons/services/redirect/short-colored-dark.svg",
            "product/brand/icons/services/redirect/short-colored-dark.png",
            "product/brand/icons/services/redirect/short-monochrome-dark.svg",
            "product/brand/icons/services/redirect/short-monochrome-dark.png",
            "product/brand/icons/short-colored-dark.svg",
            "product/brand/icons/short-colored-dark.png",
            "product/brand/icons/short-monochrome-dark.svg",
            "product/brand/icons/short-monochrome-dark.png",
            "product/brand/icons/services/redirect/short-colored-light.svg",
            "product/brand/icons/services/redirect/short-colored-light.png",
            "product/brand/icons/services/redirect/short-monochrome-light.svg",
            "product/brand/icons/services/redirect/short-monochrome-light.png",
            "product/brand/icons/short-colored-light.svg",
            "product/brand/icons/short-colored-light.png",
            "product/brand/icons/short-monochrome-light.svg",
            "product/brand/icons/short-monochrome-light.png");
    }

    [Fact]
    public void Candidates_AFullRequestTriesTheOtherColorBeforeTheShortForm()
    {
        var candidates = ProductIconPaths.Candidates(new ProductIconRequest { Form = IconForm.Full, Color = IconColor.Monochrome });

        candidates.Take(8).Select(path => Path.GetFileName(path)).Should().Equal(
            "full-monochrome-light.svg", "full-monochrome-light.png",
            "full-colored-light.svg", "full-colored-light.png",
            "short-monochrome-light.svg", "short-monochrome-light.png",
            "short-colored-light.svg", "short-colored-light.png");
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("Redirect")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Candidates_RefuseAServiceNameThatCouldLeaveTheIconFolder(string service)
    {
        var act = () => ProductIconPaths.Candidates(new ProductIconRequest { Service = service });

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("product/brand/icons/short-colored-dark.svg", true)]
    [InlineData("product/brand/icons/full-monochrome-light.png", true)]
    [InlineData("product/brand/icons/services/redirect/short-colored-dark.png", true)]
    [InlineData("product/brand/icons/short-colored-dark.webp", false)]
    [InlineData("product/brand/icons/Short-Colored-Dark.svg", false)]
    [InlineData("product/brand/icons/services/redirect/nested/short-colored-dark.svg", false)]
    [InlineData("product/brand/icons/wheelhouse-mark.png", false)]
    [InlineData("engineering/codebase/app.frontend-services/apps/web/public/favicon.svg", true)]
    [InlineData("engineering/codebase/app.frontend-services/apps/web/public/favicon-dark.png", true)]
    [InlineData("engineering/codebase/app.frontend-services/apps/web/public/favicon.ico", false)]
    [InlineData("engineering/codebase/app.frontend-services/apps/web/node_modules/x/public/favicon.svg", false)]
    [InlineData("docs/logo.png", false)]
    public void IsCandidate_KeepsOnlyFilesAnIconCanComeFrom(string path, bool expected)
    {
        ProductIconPaths.IsCandidate(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("a/short-colored-dark.svg", "image/svg+xml")]
    [InlineData("a/short-colored-dark.png", "image/png")]
    [InlineData("a/favicon.ico", "application/octet-stream")]
    public void ContentType_FollowsTheExtension(string path, string expected)
    {
        ProductIconPaths.ContentType(path).Should().Be(expected);
    }
}
