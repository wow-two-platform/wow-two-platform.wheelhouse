using AwesomeAssertions;
using Wheelhouse.Application.Deployments;
using Wheelhouse.Application.Products;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="ProductRepositories"/> — a product built from several repositories (decision 70), the
/// rules the runner's <c>catalog.py</c> and <c>release_sets.py</c> apply too — and the release set labels a changelog
/// takes (<see cref="DeploymentChangelogQueryValidator"/>).</summary>
public sealed class ProductRepositoriesTests
{
    private static ProductReleaseValueObject Release(string repository, params string[] services) => new()
    {
        Asset = "ocharo-release.tar.gz",
        Images = [.. services.Select(service => new ReleaseImageValueObject { Service = service, Image = "ghcr.io/" + repository + "/" + service })],
    };

    [Theory]
    [InlineData("ocharo", "ocharo-hq/ocharo-studio", "studio")]
    [InlineData("ocharo", "ocharo-hq/Ocharo.Assets", "assets")]
    [InlineData("wheelhouse", "wow-two-platform/wow-two-platform.wheelhouse", "wheelhouse")]
    [InlineData("pilot", "owner/pilot", "pilot")]
    [InlineData("pilot", "owner/2d2", "r2d2")]
    [InlineData("pilot", "owner/pilot_x.y", "x-y")]
    public void ShortName_ShouldDropTheOwnersAndTheProductsPrefix(string product, string repository, string expected) =>
        ProductRepositories.ShortName(product, repository).Should().Be(expected);

    [Fact]
    public void Of_ShouldListThePrimaryRepositoryFirst()
    {
        var studio = new ProductComponentValueObject { Repository = "ocharo-hq/ocharo-studio", Release = Release("ocharo-hq/ocharo-studio", "studio") };

        ProductRepositories.Of("ocharo-hq/ocharo-platform", null, [studio]).Select(item => item.Repository)
            .Should().Equal("ocharo-hq/ocharo-platform", "ocharo-hq/ocharo-studio");
        ProductRepositories.Of("owner/pilot", null, null).Should().ContainSingle();
    }

    [Fact]
    public void Problem_ShouldNameAServiceInTwoRepositories_ARepeatedRepository_OrNothing()
    {
        (string, ProductReleaseValueObject?) platform = ("ocharo-hq/ocharo-platform", Release("ocharo-hq/ocharo-platform", "desk", "jobs"));

        ProductRepositories.Problem("ocharo", [platform, ("ocharo-hq/ocharo-studio", Release("ocharo-hq/ocharo-studio", "studio"))])
            .Should().BeNull();
        ProductRepositories.Problem("ocharo", [platform, ("ocharo-hq/ocharo-studio", Release("ocharo-hq/ocharo-studio", "jobs"))])
            .Should().Be("Service 'jobs' is in both ocharo-hq/ocharo-platform and ocharo-hq/ocharo-studio; a service name belongs to one repository of the product.");
        ProductRepositories.Problem("ocharo", [platform, ("OCHARO-HQ/ocharo-platform", null)])
            .Should().Be("A product names each repository once.");
        ProductRepositories.Problem("ocharo", [platform, ("other/ocharo-platform", null)])
            .Should().Be("Two repositories of the product share a short name.");
    }

    [Theory]
    [InlineData("v1.2.0", true)]
    [InlineData("platform-v0.1.8_studio-v0.1.6", true)]
    [InlineData("platform-v0.1.9_studio-sha-abcdef1_assets-v0.1.0-rc.1", true)]
    [InlineData("set-0123456789ab", true)]
    [InlineData("platform_v1", false)]
    [InlineData("set-xyz", false)]
    [InlineData("platform-v0.1.8__studio-v0.1.6", false)]
    public void ChangelogLabels_ShouldTakeATagOrAReleaseSet(string label, bool valid)
    {
        var result = new DeploymentChangelogQueryValidator().Validate(new DeploymentChangelogQuery("ocharo", null, label));

        result.IsValid.Should().Be(valid);
    }
}
