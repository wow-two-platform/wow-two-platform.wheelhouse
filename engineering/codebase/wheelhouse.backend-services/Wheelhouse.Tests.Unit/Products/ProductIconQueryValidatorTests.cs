using AwesomeAssertions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using Wheelhouse.Application.Products.Validators;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="ProductIconQueryValidator"/>.</summary>
public sealed class ProductIconQueryValidatorTests
{
    private readonly ProductIconQueryValidator _validator = new();

    [Fact]
    public void Validate_ShouldPass_ForAProductIconAndAServiceIcon()
    {
        _validator.Validate(new ProductIconQuery { Slug = "foreverpin" }).IsValid.Should().BeTrue();
        _validator.Validate(new ProductIconQuery
        {
            Slug = "foreverpin",
            Icon = new ProductIconRequest { Service = "redirect", Theme = IconTheme.Dark, Form = IconForm.Full, Color = IconColor.Monochrome },
        }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("Redirect")]
    [InlineData("")]
    public void Validate_ShouldFail_WhenTheServiceIsNotAServiceName(string service)
    {
        var result = _validator.Validate(new ProductIconQuery { Slug = "foreverpin", Icon = new ProductIconRequest { Service = service } });

        result.Errors.Should().ContainSingle(error => error.PropertyName == "Icon.Service");
    }

    [Fact]
    public void Validate_ShouldFail_WhenAVariantAxisIsOutsideItsSet()
    {
        var result = _validator.Validate(new ProductIconQuery
        {
            Slug = "foreverpin",
            Icon = new ProductIconRequest { Theme = (IconTheme)7, Form = (IconForm)7, Color = (IconColor)7 },
        });

        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo("Icon.Theme", "Icon.Form", "Icon.Color");
    }
}
