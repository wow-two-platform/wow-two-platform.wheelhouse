using FluentValidation;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Servers.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Servers.Validators;

/// <summary>Validates a request to add a host Wheelhouse deploys to.</summary>
public sealed class ServerCreateCommandValidator : AbstractValidator<ServerCreateCommand>
{
    /// <summary>Configures the server rules.</summary>
    public ServerCreateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeNameSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Provider).IsInEnum();
        RuleFor(x => x.Host).MustMatch(FormatPatternConstants.NetworkHost, "a host name or address");
        RuleFor(x => x.Region).MustMatch(FormatPatternConstants.RegionCode, "a region such as hel1");
        RuleFor(x => x.SshUser).MustMatch(FormatPatternConstants.UnixUserName, "a lowercase user name");
        RuleFor(x => x.SshPort).InclusiveBetween(1, 65535);
        RuleFor(x => x.Ingress).NotNull().SetValidator(new ServerIngressValueObjectValidator());
        RuleFor(x => x.ConsoleUrl).MustBeHttpsAddress("https://www.ovh.com/manager/");
    }
}
