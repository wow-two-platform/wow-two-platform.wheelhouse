using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Wheelhouse.Application.Deployments.Settings.CommandHandlers;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Domain.Settings.Enums;
using Wheelhouse.Infrastructure.Deployments;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.Settings;

/// <summary>Tests for generated and linked keys' pure rules: N random bytes in their encoding
/// (<see cref="SecretValues.RandomBytes"/>), a PEM certificate's end date as the version's expiry
/// (<see cref="SecretValues.CertificateEnd"/>, <see cref="GeneratedSettingsService.Write"/>), the runner's
/// <c>generate-setting</c> answer (<see cref="GeneratedSettingReply"/>), and a save to a linked key refused.</summary>
public sealed class GeneratedSettingsTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void RandomBytes_ShouldEncodeExactlyNBytes_AsCanonicalBase64UrlOrHex(int bytes)
    {
        var base64 = SecretValues.RandomBytes(bytes, SecretValues.Base64Url);
        base64.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        System.Buffers.Text.Base64Url.DecodeFromChars(base64).Should().HaveCount(bytes);
        System.Buffers.Text.Base64Url.EncodeToString(System.Buffers.Text.Base64Url.DecodeFromChars(base64)).Should().Be(base64,
            "a service decoding the value gets the same bytes back");

        var hex = SecretValues.RandomBytes(bytes, SecretValues.Hex);
        hex.Should().MatchRegex("^[0-9a-f]{" + bytes * 2 + "}$");
        Enumerable.Range(0, 10).Select(_ => SecretValues.RandomBytes(bytes, SecretValues.Hex)).Distinct().Should().HaveCount(10);
    }

    [Theory]
    [InlineData(15, "base64url")]
    [InlineData(65, "hex")]
    [InlineData(32, "words")]
    public void RandomBytes_ShouldRefuseASizeOrEncodingOutsideTheDescriptorsRange(int bytes, string format) =>
        FluentActions.Invoking(() => SecretValues.RandomBytes(bytes, format)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void CertificateEnd_ShouldReadTheCertificatesNotAfter_BesideItsPrivateKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var notAfter = new DateTimeOffset(2027, 3, 4, 5, 6, 7, TimeSpan.Zero);
        using var certificate = SelfSigned(key, "CN=signing", notAfter);
        var pem = key.ExportPkcs8PrivateKeyPem() + "\n" + certificate.ExportCertificatePem() + "\n";

        SecretValues.CertificateEnd(pem).Should().Be(notAfter);
        var write = GeneratedSettingsService.Write("Identity:Server:Keys:Signing:0", new GeneratedSettingValue(pem), "rotated");
        (write.Key, write.Value, write.Note, write.Expiry).Should().Be(("Identity:Server:Keys:Signing:0", pem, "rotated", new ExpiryChange(notAfter)));
        write.ToString().Should().NotContain("PRIVATE KEY", "a write never prints its value");
    }

    [Fact]
    public void CertificateEnd_ShouldTakeTheEarliestEnd_AndIgnoreValuesWithoutACertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var soon = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var leaf = SelfSigned(key, "CN=leaf", soon);
        using var root = SelfSigned(key, "CN=root", later);

        SecretValues.CertificateEnd(root.ExportCertificatePem() + "\n" + leaf.ExportCertificatePem()).Should().Be(soon);
        SecretValues.CertificateEnd("not a certificate").Should().BeNull();
        SecretValues.CertificateEnd(key.ExportPkcs8PrivateKeyPem()).Should().BeNull("a key alone has no end date");
        SecretValues.CertificateEnd("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----").Should().BeNull();
        SecretValues.CertificateEnd(null).Should().BeNull();
        GeneratedSettingsService.Write("A:Key", new GeneratedSettingValue("plain-random"), null).Expiry
            .Should().BeNull("a value without a certificate keeps the secret's expiry");
    }

    [Fact]
    public void Reply_ShouldTakeTheValueOnlyFromACleanExit()
    {
        var value = Success(GeneratedSettingReply.Read(0, "{\"value\": \"line1\\nline2\"}\n", ""));
        value.Value.Should().Be("line1\nline2");
        value.ToString().Should().NotContain("line1");

        Fail(GeneratedSettingReply.Read(0, "{\"value\": \"\"}", "")).Message.Should().Be("the runner answered no value.");
        Fail(GeneratedSettingReply.Read(0, "SECRET_OUTPUT", "")).Message.Should().NotContain("SECRET_OUTPUT");
    }

    [Theory]
    [InlineData("{\"error\": \"keys generate: unknown role\"}", "", AppErrorType.ExternalUnavailable, "keys generate: unknown role")]
    [InlineData("", "{\"error\": \"command timed out after 120s\"}", AppErrorType.ExternalUnavailable, "command timed out after 120s")]
    [InlineData("", "{\"failure\": \"Rejected\", \"reason\": \"Unknown service\"}", AppErrorType.Conflict, "Unknown service")]
    [InlineData("{\"value\": \"LEAKED\", \"error\": \"exit 2\"}", "", AppErrorType.ExternalUnavailable, "exit 2")]
    [InlineData("LEAKED", "LEAKED too", AppErrorType.ExternalUnavailable, "the runner failed without an error line (exit 1).")]
    public void Reply_ShouldKeepOnlyTheRunnersErrorLine_WhenItFails(string output, string error, AppErrorType type, string message)
    {
        var failure = Fail(GeneratedSettingReply.Read(1, output, error));
        (failure.Type, failure.Message).Should().Be((type, message));
        failure.Message.Should().NotContain("LEAKED");
    }

    [Fact]
    public void Reply_ShouldStripControlCharacters_AndBoundTheErrorLine()
    {
        var failure = Fail(GeneratedSettingReply.Read(2, "{\"error\": \"a\\u001b[31m" + new string('x', 400) + "\"}", ""));
        failure.Message.Should().NotContain("\u001b");
        failure.Message.Length.Should().Be(GeneratedSettingReply.MaxReasonLength);
    }

    [Fact]
    public void Check_ShouldRefuseAValueForALinkedKey_ButLetItsOldValueBeRemoved()
    {
        var declared = new Dictionary<string, KeyDeclaration>
        {
            ["Auth:ClientSecret"] = new("Auth:ClientSecret", SettingType.Text, null, null, false, false, false)
            {
                From = "Identity:Server:Clients:0:ClientSecret",
            },
        };
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Auth:ClientSecret", "x", true)], declared)
            .Should().Equal("Auth:ClientSecret takes its value from Identity:Server:Clients:0:ClientSecret; set Identity:Server:Clients:0:ClientSecret instead.");
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Auth:ClientSecret", "", true) { Remove = true }], declared)
            .Should().BeEmpty();
    }

    [Fact]
    public void Generate_ShouldNameItsFormForTheAudit_AndBoundACommandsTime()
    {
        new SettingGenerateDto(32, SecretValues.Base64Url).Kind.Should().Be("random 32 bytes base64url");
        var command = new SettingGenerateDto(Command: ["dotnet", "Identity.Api.dll", "keys", "generate"]);
        (command.IsCommand, command.Kind, command.Timeout).Should().Be((true, "command", TimeSpan.FromMinutes(2)));
        (command with { TimeoutSeconds = 3600 }).Timeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    private static X509Certificate2 SelfSigned(ECDsa key, string subject, DateTimeOffset notAfter) =>
        new CertificateRequest(subject, key, HashAlgorithmName.SHA256).CreateSelfSigned(notAfter.AddYears(-1), notAfter);

    private static GeneratedSettingValue Success(AppResult<GeneratedSettingValue> result) =>
        result.Should().BeOfType<AppResult<GeneratedSettingValue>.Success>().Subject.Data;

    private static AppError Fail(AppResult<GeneratedSettingValue> result) =>
        result.Should().BeOfType<AppResult<GeneratedSettingValue>.Failure>().Subject.Error;
}
