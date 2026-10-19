using AwesomeAssertions;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Tests.Unit.Settings;

/// <summary>Tests for <see cref="SessionCookies"/>: the session cookie's name per environment.</summary>
public sealed class SessionCookiesTests
{
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
        SessionCookies.SessionCookieName(environment).Should().Be(expected);
    }
}
