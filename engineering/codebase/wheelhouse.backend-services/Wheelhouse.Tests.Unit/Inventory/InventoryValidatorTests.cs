using AwesomeAssertions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Validators;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Validators;
using Wheelhouse.Application.Targets.Commands;
using Wheelhouse.Application.Targets.Validators;
using Wheelhouse.Application.Vaults.Commands;
using Wheelhouse.Application.Vaults.Validators;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Servers.Models;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;

namespace Wheelhouse.Tests.Unit.Inventory;

/// <summary>Tests for the inventory validators: each accepts what the runner accepts and refuses what it would refuse.</summary>
public sealed class InventoryValidatorTests
{
    private static readonly ProductCreateCommand Product = new()
    {
        Slug = "pilot", Name = "Pilot", Description = "A pilot product.", Repository = "owner/pilot", DefaultBranch = "main",
        Release = new ProductReleaseValueObject
        {
            Asset = "pilot-release.tar.gz", Workflow = "publish.yml",
            Images = [new ReleaseImageValueObject { Service = "api", Image = "ghcr.io/owner/pilot/api" }],
        },
    };

    private static readonly ServerCreateCommand Server = new()
    {
        Slug = "hel1", Name = "Helsinki", Provider = VpsProvider.Hetzner, Host = "vps.example.net", Region = "hel1",
        SshUser = "deploy", SshPort = 22,
        Ingress = new ServerIngressValueObject { Pattern = "{site}-{product}.{environment}.preview.example", Probe = "http://ingress:80" },
    };

    private static readonly TargetCreateCommand Target = new()
    {
        Slug = "pilot-prod", Product = "pilot", Server = "hel1", Environment = DeploymentEnvironment.Prod,
        Network = "platform", Root = "/srv/wheelhouse",
        Settings = [new TargetSettingValueObject { Service = "api", Path = "/srv/settings/pilot/api.json" }],
        SmokeChecks = [new SmokeCheckValueObject { Service = "api", Path = "/health", Status = 200 }],
        Sites = [new SiteHostValueObject { Site = "app", Host = "pilot.example.com" }],
    };

    private static readonly VaultCreateCommand Vault = new() { Slug = "hel1-vault", Name = "Vault", Server = "hel1", Url = "http://vault:8080" };

    [Fact]
    public void Validate_ShouldPass_WhenEachDefinitionIsWhatTheRunnerAccepts()
    {
        new ProductCreateCommandValidator().Validate(Product).IsValid.Should().BeTrue();
        new ServerCreateCommandValidator().Validate(Server).IsValid.Should().BeTrue();
        new TargetCreateCommandValidator().Validate(Target).IsValid.Should().BeTrue();
        new VaultCreateCommandValidator().Validate(Vault).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProductValidate_ShouldFail_WhenTheSlugRepositoryOrReleaseIsMalformed()
    {
        var validator = new ProductCreateCommandValidator();
        validator.Validate(Product with { Slug = "Pilot" }).IsValid.Should().BeFalse();
        validator.Validate(Product with { Repository = "https://github.com/owner/pilot" }).IsValid.Should().BeFalse();
        validator.Validate(Product with { Release = Product.Release! with { Workflow = "build.sh" } }).IsValid.Should().BeFalse();
        validator.Validate(Product with
        {
            Release = Product.Release! with
            {
                Images =
                [
                    new ReleaseImageValueObject { Service = "api", Image = "ghcr.io/owner/pilot/api" },
                    new ReleaseImageValueObject { Service = "api", Image = "ghcr.io/owner/pilot/other" },
                ],
            },
        }).IsValid.Should().BeFalse();
        validator.Validate(Product with
        {
            Release = Product.Release! with { Images = [new ReleaseImageValueObject { Service = "api", Image = "NOT AN IMAGE" }] },
        }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ProductValidate_ShouldPass_WhenThereIsNoReleaseSource()
    {
        new ProductCreateCommandValidator().Validate(Product with { Release = null }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("bad host", 22, "{site}.example", "http://ingress:80")]
    [InlineData("vps.example.net", 0, "{site}.example", "http://ingress:80")]
    [InlineData("vps.example.net", 22, "app.example", "http://ingress:80")]
    [InlineData("vps.example.net", 22, "{site}.example", "ingress:80")]
    public void ServerValidate_ShouldFail_WhenTheHostPortPatternOrProbeIsMalformed(string host, int port, string pattern, string probe)
    {
        var command = Server with { Host = host, SshPort = port, Ingress = Server.Ingress with { Pattern = pattern, Probe = probe } };

        new ServerCreateCommandValidator().Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TargetValidate_ShouldFail_WhenTheRootSettingsSmokeOrSitesAreMalformed()
    {
        var validator = new TargetCreateCommandValidator();
        validator.Validate(Target with { Root = "/" }).IsValid.Should().BeFalse();
        validator.Validate(Target with { Root = "/srv/../etc" }).IsValid.Should().BeFalse();
        validator.Validate(Target with { Settings = [new TargetSettingValueObject { Service = "api", Path = "settings/api.json" }] })
            .IsValid.Should().BeFalse();
        validator.Validate(Target with
        {
            Settings =
            [
                new TargetSettingValueObject { Service = "api", Path = "/a.json" },
                new TargetSettingValueObject { Service = "api", Path = "/b.json" },
            ],
        }).IsValid.Should().BeFalse();
        validator.Validate(Target with { SmokeChecks = [new SmokeCheckValueObject { Service = "api", Path = "/health", Status = 503 }] })
            .IsValid.Should().BeFalse();
        validator.Validate(Target with { Sites = [new SiteHostValueObject { Site = "app", Host = "App.Example.com" }] })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void VaultValidate_ShouldFail_WhenTheUrlCarriesAPath()
    {
        new VaultCreateCommandValidator().Validate(Vault with { Url = "http://vault:8080/admin" }).IsValid.Should().BeFalse();
    }
}
