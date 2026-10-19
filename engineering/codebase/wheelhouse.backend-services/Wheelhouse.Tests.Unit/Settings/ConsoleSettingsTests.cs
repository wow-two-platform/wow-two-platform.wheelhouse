using AwesomeAssertions;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Tests.Unit.Settings;

/// <summary>Tests for <see cref="ConsoleSettings"/>: the other console's address the environment tag switches to.</summary>
public sealed class ConsoleSettingsTests
{
    [Theory]
    [InlineData("https://wheelhouse.example", "https://wheelhouse.example")]
    [InlineData("https://wheelhouse.example/", "https://wheelhouse.example")]
    [InlineData("HTTPS://Wheelhouse.Example:443", "https://wheelhouse.example")]
    [InlineData("https://wheelhouse.example:8443", "https://wheelhouse.example:8443")]
    [InlineData("  https://wheelhouse.example:8443/  ", "https://wheelhouse.example:8443")]
    public void PeerOrigin_ShouldKeepOnlySchemeHostAndPort(string value, string expected)
    {
        var settings = new ConsoleSettings { PeerUrl = value };

        settings.IsValid.Should().BeTrue();
        settings.PeerOrigin.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PeerOrigin_ShouldBeNull_WhenNoPeerIsConfigured(string value)
    {
        var settings = new ConsoleSettings { PeerUrl = value };

        settings.IsValid.Should().BeTrue("an empty setting leaves the switch off");
        settings.PeerOrigin.Should().BeNull();
    }

    [Fact]
    public void PeerUrl_ShouldDefaultToEmpty()
    {
        var settings = new ConsoleSettings();

        settings.IsValid.Should().BeTrue();
        settings.PeerOrigin.Should().BeNull();
    }

    [Theory]
    [InlineData("http://wheelhouse.example")]
    [InlineData("http://localhost:18210")]
    [InlineData("wheelhouse.example")]
    [InlineData("/p/foreverpin")]
    [InlineData("ftp://wheelhouse.example")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://wheelhouse.example/p/foreverpin")]
    [InlineData("https://wheelhouse.example?next=1")]
    [InlineData("https://wheelhouse.example#top")]
    [InlineData("https://owner:secret@wheelhouse.example")]
    public void PeerUrl_ShouldBeInvalid_WhenItIsNotAnAbsoluteHttpsOrigin(string value)
    {
        var settings = new ConsoleSettings { PeerUrl = value };

        settings.IsValid.Should().BeFalse();
        settings.PeerOrigin.Should().BeNull();
    }

    [Theory]
    [InlineData(null, ".wheelhouse.auth")]
    [InlineData("", ".wheelhouse.auth")]
    [InlineData("prod", ".wheelhouse.auth")]
    [InlineData("PROD", ".wheelhouse.auth")]
    [InlineData("dev", ".wheelhouse.auth.dev")]
    [InlineData(" Dev ", ".wheelhouse.auth.dev")]
    [InlineData("dev;path=/", ".wheelhouse.auth")]
    public void SessionCookieName_ShouldSeparateDevFromProd_WhenTheyShareOneHostName(string? environment, string expected)
    {
        ConsoleSettings.SessionCookieName(environment).Should().Be(expected);
    }
}
