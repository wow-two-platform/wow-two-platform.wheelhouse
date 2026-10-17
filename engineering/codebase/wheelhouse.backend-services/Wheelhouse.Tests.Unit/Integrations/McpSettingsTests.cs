using AwesomeAssertions;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Tests.Unit.Integrations;

/// <summary>Tests for <see cref="McpSettings"/>: which browser origins may call the MCP endpoint.</summary>
public sealed class McpSettingsTests
{
    [Theory]
    [InlineData("https://wheelhouse.tail1234.ts.net", "https://wheelhouse.tail1234.ts.net")]
    [InlineData("HTTPS://Wheelhouse.Example:443/", "https://wheelhouse.example")]
    [InlineData("https://wheelhouse.example:8443", "https://wheelhouse.example:8443")]
    [InlineData("http://localhost:6274", "http://localhost:6274")]
    public void NormalizeOrigin_ShouldKeepOnlySchemeHostAndPort(string value, string expected) =>
        McpSettings.NormalizeOrigin(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("wheelhouse.example")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://wheelhouse.example")]
    [InlineData("https://wheelhouse.example/mcp")]
    [InlineData("https://wheelhouse.example?next=1")]
    [InlineData("https://operator@wheelhouse.example")]
    public void NormalizeOrigin_ShouldRefuseAnythingButAnHttpOrigin(string? value) =>
        McpSettings.NormalizeOrigin(value).Should().BeNull();

    [Fact]
    public void Allows_ShouldAcceptOnlyTheConsolesOwnOriginAndConfiguredOnes()
    {
        var settings = new McpSettings { AllowedOrigins = ["http://localhost:6274"] };
        const string console = "https://wheelhouse.example:8443";

        settings.Allows("https://wheelhouse.example:8443", console).Should().BeTrue();
        settings.Allows("http://localhost:6274", console).Should().BeTrue();
        settings.Allows("https://rebound.example", console).Should().BeFalse();
        settings.Allows("https://wheelhouse.example", console).Should().BeFalse();
        settings.Allows("http://wheelhouse.example:8443", console).Should().BeFalse();
        settings.Allows("null", console).Should().BeFalse();
    }
}
