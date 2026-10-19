using AwesomeAssertions;
using Wheelhouse.Application.Deployments.Settings.CommandHandlers;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Domain.Settings.Enums;

namespace Wheelhouse.Tests.Unit.Settings;

/// <summary>Tests for the save's checks (<see cref="DeploymentSettingsSaveCommandHandler.Check"/>) and the render
/// comparison a failed deploy's hint reads (<see cref="DeploymentSettingsService.Compare"/>).</summary>
public sealed class SettingsSaveRulesTests
{
    private static readonly Dictionary<string, KeyDeclaration> Declared = new()
    {
        ["Limits:Max"] = new("Limits:Max", SettingType.Number, null, null, false, true, false),
        ["Stripe:Account"] = new("Stripe:Account", SettingType.Text, null, null, true, false, false),
        ["Support:TokenUrl"] = new("Support:TokenUrl", SettingType.Url, null, null, false, true, false),
        ["Flags:On"] = new("Flags:On", SettingType.Boolean, null, null, false, true, false),
    };

    [Fact]
    public void Check_ShouldRefuseSecretLookingOrDeclaredSecretKeysAsPlain_UnlessConfirmed()
    {
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Smtp:Password", "x", false)], Declared)
            .Should().ContainSingle().Which.Should().Contain("reads as a secret");
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Stripe:Account", "x", false)], Declared)
            .Should().ContainSingle().Which.Should().Contain("is declared a secret");
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Smtp:Password", "x", false) { ConfirmPlain = true }], Declared)
            .Should().BeEmpty();
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Support:TokenUrl", "https://x.example", false)], Declared)
            .Should().BeEmpty("a typed key the release calls plain is plain");
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Smtp:Password", "x", true)], Declared).Should().BeEmpty();
    }

    [Fact]
    public void Check_ShouldTypeEachValue_AndPassOverRemovals()
    {
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Limits:Max", "lots", false)], Declared)
            .Should().Equal("Limits:Max must be a number, such as 42 or 0.5.");
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Flags:On", " TRUE ", false)], Declared).Should().BeEmpty();
        DeploymentSettingsSaveCommandHandler.Check([new DeploySettingValue("Limits:Max", "", false) { Remove = true }], Declared).Should().BeEmpty();
    }

    [Fact]
    public void Compare_ShouldNameAddedChangedAndRemovedKeys_WithSecretVersions()
    {
        var before = new Dictionary<string, RenderedKey>
        {
            ["A:Setting"] = new("setting", null, "aaaa"), ["B:Secret"] = new("secret", 3, "f1"), ["C:Gone"] = new("setting", null, "cc"),
            ["D:Same"] = new("secret", 1, "dd"),
        };
        var after = new Dictionary<string, RenderedKey>
        {
            ["A:Setting"] = new("setting", null, "bbbb"), ["B:Secret"] = new("secret", 4, "f2"), ["D:Same"] = new("secret", 1, "dd"),
            ["E:New"] = new("secret", 1, "ee"),
        };
        DeploymentSettingsService.Compare(before, after).Should().Equal(
            new SettingChangeDto("A:Setting", "setting", "changed"),
            new SettingChangeDto("B:Secret", "secret", "changed", 3, 4),
            new SettingChangeDto("C:Gone", "setting", "removed"),
            new SettingChangeDto("E:New", "secret", "added", null, 1));
    }
}
