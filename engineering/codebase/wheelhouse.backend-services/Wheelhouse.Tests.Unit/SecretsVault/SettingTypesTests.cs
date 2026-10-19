using AwesomeAssertions;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Domain.Settings.Enums;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>Tests for <see cref="SettingTypes"/>: what each type accepts, the date formats and cron fields it reads, and
/// which key names read as secrets. The release generator checks declared defaults by the same rules.</summary>
public sealed class SettingTypesTests
{
    [Theory]
    [InlineData(SettingType.Number, "42", true)]
    [InlineData(SettingType.Number, "-0.25", true)]
    [InlineData(SettingType.Number, "007", false)]
    [InlineData(SettingType.Number, "1e3", false)]
    [InlineData(SettingType.Boolean, "true", true)]
    [InlineData(SettingType.Boolean, "True", false)]
    [InlineData(SettingType.Url, "https://example.com/path?q=1", true)]
    [InlineData(SettingType.Url, "ftp://example.com", false)]
    [InlineData(SettingType.Url, "example.com", false)]
    [InlineData(SettingType.Url, "https://exa mple.com", false)]
    [InlineData(SettingType.Email, "owner@example.co.uk", true)]
    [InlineData(SettingType.Email, "owner@localhost", false)]
    [InlineData(SettingType.Date, "2027-02-28", true)]
    [InlineData(SettingType.Date, "2027-02-30", false)]
    [InlineData(SettingType.Text, "anything at all", true)]
    public void Check_ShouldAcceptOnlyValuesOfTheirType(SettingType type, string value, bool fits) =>
        (SettingTypes.Check(type, value) is null).Should().Be(fits);

    [Fact]
    public void Check_ShouldReadChoicesAndDateFormats_AndNeverQuoteTheValue()
    {
        SettingTypes.Check(SettingType.Choice, "live", choices: ["test", "live"]).Should().BeNull();
        SettingTypes.Check(SettingType.Choice, "sandbox", choices: ["test", "live"]).Should().Be("must be one of test, live");
        SettingTypes.Check(SettingType.Date, "31.01.2027 18:30", "dd.MM.yyyy HH:mm").Should().BeNull();
        SettingTypes.Check(SettingType.Date, "2027-01-31", "dd.MM.yyyy").Should().Be("must be a date as dd.MM.yyyy");
        SettingTypes.Check(SettingType.Number, "hunter2").Should().NotContain("hunter2");
    }

    [Theory]
    [InlineData("yyyy-MM-dd", true)]
    [InlineData("dd.MM.yyyy HH:mm:ss", true)]
    [InlineData("yyyy-MM-ddTHH:mm", true)]
    [InlineData("MM/yyyy", false)]
    [InlineData("%Y-%m-%d", false)]
    [InlineData("yyyy-MMM-dd", false)]
    public void IsDateFormat_ShouldTakeTheSharedTokensOnly(string format, bool valid) =>
        SettingTypes.IsDateFormat(format).Should().Be(valid);

    [Theory]
    [InlineData("0 3 * * *", true)]
    [InlineData("*/15 9-17 * * MON-FRI", true)]
    [InlineData("0 0 1 JAN,JUL *", true)]
    [InlineData("0 30 2 L * ?", true)]
    [InlineData("0 0 * * 5#2", true)]
    [InlineData("0 0 15W * *", true)]
    [InlineData("@daily", true)]
    [InlineData("* * * *", false)]
    [InlineData("60 * * * *", false)]
    [InlineData("* 24 * * *", false)]
    [InlineData("0 0 0 * *", false)]
    [InlineData("5-1 * * * *", false)]
    [InlineData("*/0 * * * *", false)]
    [InlineData("@often", false)]
    [InlineData(" 0 3 * * *", false)]
    [InlineData("0 3 * * * * *", false)]
    public void IsCron_ShouldReadFiveOrSixFieldsWithinTheirRanges(string value, bool valid) =>
        SettingTypes.IsCron(value).Should().Be(valid);

    [Theory]
    [InlineData("Billing:SecretKey", true)]
    [InlineData("Smtp:Password", true)]
    [InlineData("Github:Token", true)]
    [InlineData("Database:ConnectionString", true)]
    [InlineData("ConnectionStrings:Reports", true)]
    [InlineData("Auth:Credentials", true)]
    [InlineData("Auth:Google:ClientId", false)]
    [InlineData("DataProtection:KeysPath", false)]
    [InlineData("Billing:Prices:Solo", false)]
    public void LooksSecret_ShouldReadTheLastSectionLikeTheRunner(string key, bool secret) =>
        SettingTypes.LooksSecret(key).Should().Be(secret);

    [Fact]
    public void Parse_ShouldKnowEveryTypeName_AndLinkAsUrl()
    {
        SettingTypes.Names.Select(SettingTypes.Parse).Should().NotContainNulls();
        SettingTypes.Parse("link").Should().Be(SettingType.Url);
        SettingTypes.Parse("colour").Should().BeNull();
        SettingTypes.Parse(null).Should().Be(SettingType.Text);
    }
}
