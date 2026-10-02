using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Application.Servers.Commands;

namespace Wheelhouse.Application.Servers.Validators;

/// <summary>Validates a request to add a host Wheelhouse deploys to.</summary>
public sealed class ServerCreateCommandValidator : AbstractValidator<ServerCreateCommand>
{
    /// <summary>Configures the server rules.</summary>
    public ServerCreateCommandValidator()
    {
        RuleFor(x => x.Slug).MustBeSlug();
        RuleFor(x => x.Name).MustBeDisplayName();
        RuleFor(x => x.Provider).IsInEnum();
        RuleFor(x => x.Host).MustMatch(InventoryPatternConstants.SshHost, "a host name or address");
        RuleFor(x => x.Region).MustMatch(InventoryPatternConstants.Region, "a region such as hel1");
        RuleFor(x => x.SshUser).MustMatch(InventoryPatternConstants.SshUser, "a lowercase user name");
        RuleFor(x => x.SshPort).InclusiveBetween(1, 65535);
        RuleFor(x => x.Ingress).NotNull().SetValidator(new ServerIngressValueObjectValidator());
    }
}
