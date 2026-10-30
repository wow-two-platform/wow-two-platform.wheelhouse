using FluentValidation;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Servers.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Servers.Validators;

/// <summary>Validates an enrollment request; the runner checks it again before anything is recorded.</summary>
public sealed class ServerEnrollCommandValidator : AbstractValidator<ServerEnrollCommand>
{
    /// <summary>Configures the rules.</summary>
    public ServerEnrollCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeNameSlug();
        RuleFor(x => x.PublicAddress).Matches(FormatPatternConstants.Ipv4Address)
            .WithMessage("'Public Address' must be the server's public IPv4 address.")
            .Must(address => !address.StartsWith("100.", StringComparison.Ordinal) || !InTailnet(address))
            .WithMessage("'Public Address' must be the public address, not a tailnet one.");
        RuleFor(x => x.SshPort).InclusiveBetween(1, 65535);
        RuleFor(x => x.LoginUser).Matches(FormatPatternConstants.UnixUserName)
            .WithMessage("'Login User' must be root or the image's administrator, such as ubuntu.");
        RuleFor(x => x.Admin).Matches(InfraPatterns.Admin).WithMessage("'Admin' must be a lowercase account name.")
            .Must(admin => !InfraPatterns.ReservedAdmins.Contains(admin))
            .WithMessage("'Admin' must be something other than root or wheelhouse-deploy.");
        RuleFor(x => x.Admin).Equal(x => x.LoginUser).When(x => x.LoginUser != "root")
            .WithMessage("'Admin' must be the sign-in account when that account is not root.");
        RuleFor(x => x.AdminKey!.Trim()).Matches(FormatPatternConstants.SshPublicKey).MaximumLength(1000)
            .When(x => !string.IsNullOrWhiteSpace(x.AdminKey)).OverridePropertyName(nameof(ServerEnrollCommand.AdminKey))
            .WithMessage("'Admin Key' must be one SSH public key line.");
        RuleFor(x => x.TailnetHostname).Matches(FormatPatternConstants.HostLabel)
            .WithMessage("'Tailnet Hostname' must be a lowercase host name, such as ocharo-vps.");
        RuleFor(x => x.ExpectedHostKey!.Trim()).Matches(FormatPatternConstants.SshKeyFingerprint)
            .When(x => !string.IsNullOrWhiteSpace(x.ExpectedHostKey))
            .OverridePropertyName(nameof(ServerEnrollCommand.ExpectedHostKey))
            .WithMessage("'Expected Host Key' must be a fingerprint such as SHA256:….");
    }

    private static bool InTailnet(string address)
    {
        var second = int.Parse(address.Split('.')[1], System.Globalization.CultureInfo.InvariantCulture);
        return second is >= 64 and <= 127;
    }
}
