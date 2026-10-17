using AwesomeAssertions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Validators;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="ProductStatusUpdateCommandValidator"/> and <see cref="ServiceStatusUpdateCommandValidator"/>.</summary>
public sealed class ProductStatusUpdateCommandValidatorTests
{
    private readonly ProductStatusUpdateCommandValidator _product = new();
    private readonly ServiceStatusUpdateCommandValidator _service = new();

    [Theory]
    [InlineData("foreverpin")]
    [InlineData("smart-qr-2")]
    [InlineData("0199e5a4-0000-7000-8000-000000000001")]
    public void Validate_ShouldPass_WhenTheProductIsNamedBySlugOrId(string product)
    {
        _product.Validate(new ProductStatusUpdateCommand { Product = product, Status = ProductStatus.Inactive }).IsValid.Should().BeTrue();
        _service.Validate(new ServiceStatusUpdateCommand { Product = product, Service = "api", Status = ProductStatus.Active })
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ForeverPin")]
    [InlineData("2fast")]
    [InlineData("foreverpin/../x")]
    public void Validate_ShouldFail_WhenTheProductIsNeitherASlugNorAnId(string product)
    {
        _product.Validate(new ProductStatusUpdateCommand { Product = product, Status = ProductStatus.Active })
            .Errors.Should().ContainSingle(error => error.PropertyName == nameof(ProductStatusUpdateCommand.Product));
    }

    [Fact]
    public void Validate_ShouldFail_WhenTheStatusIsOutsideTheSet()
    {
        _product.Validate(new ProductStatusUpdateCommand { Product = "foreverpin", Status = (ProductStatus)42 })
            .Errors.Should().ContainSingle(error => error.PropertyName == nameof(ProductStatusUpdateCommand.Status));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("../api")]
    [InlineData("")]
    public void Validate_ShouldFail_WhenTheServiceKeyIsNotASlug(string service)
    {
        _service.Validate(new ServiceStatusUpdateCommand { Product = "foreverpin", Service = service, Status = ProductStatus.Active })
            .Errors.Should().ContainSingle(error => error.PropertyName == nameof(ServiceStatusUpdateCommand.Service));
    }
}
