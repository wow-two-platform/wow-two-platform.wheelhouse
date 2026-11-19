using AwesomeAssertions;
using Wheelhouse.Application.Integrations.Commands;
using Wheelhouse.Application.Integrations.Validators;
using Wheelhouse.Domain.Integrations.Constants;

namespace Wheelhouse.Tests.Unit.Integrations;

/// <summary>Tests for <see cref="IntegrationKeyCreateCommandValidator"/>.</summary>
public sealed class IntegrationKeyCreateCommandValidatorTests
{
    private readonly IntegrationKeyCreateCommandValidator _validator = new();

    private static IntegrationKeyCreateCommand With(string name, params string[] scopes) => new() { Name = name, Scopes = scopes };

    [Theory]
    [InlineData("Claude")]
    [InlineData("codex agent")]
    [InlineData("ops-bot_2.1")]
    public void Validate_ShouldPass_WhenNameIsPlainAndScopesAreGranted(string name)
    {
        var result = _validator.Validate(With(name, IntegrationScopeConstants.CatalogRead));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading-space")]
    [InlineData("key:admin")]
    [InlineData("<script>")]
    public void Validate_ShouldFail_WhenNameIsNotPlain(string name)
    {
        var result = _validator.Validate(With(name, IntegrationScopeConstants.CatalogRead));

        result.Errors.Should().ContainSingle(error => error.PropertyName == nameof(IntegrationKeyCreateCommand.Name));
    }

    [Theory]
    [InlineData(IntegrationScopeConstants.DeploymentsRead)]
    [InlineData(IntegrationScopeConstants.SettingsWrite)]
    public void Validate_ShouldPass_WhenEachGrantedScopeIsChosen(string scope)
    {
        var result = _validator.Validate(With("wow2-cli", scope));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenNoScopeIsChosen()
    {
        var result = _validator.Validate(With("Claude"));

        result.Errors.Should().ContainSingle(error => error.PropertyName == nameof(IntegrationKeyCreateCommand.Scopes));
    }

    [Theory]
    [InlineData("deployments:write")]
    [InlineData("CATALOG:READ")]
    [InlineData("")]
    public void Validate_ShouldFail_WhenAScopeIsNotGranted(string scope)
    {
        var result = _validator.Validate(With("Claude", IntegrationScopeConstants.CatalogRead, scope));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().OnlyContain(error => error.PropertyName.StartsWith(nameof(IntegrationKeyCreateCommand.Scopes)));
    }
}
