using FluentValidation;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Domain.Servers.Models;

namespace Wheelhouse.Application.Servers.Validators;

/// <summary>Validates how a server's ingress publishes sites.</summary>
public sealed class ServerIngressValueObjectValidator : AbstractValidator<ServerIngressValueObject>
{
    /// <summary>Configures the ingress rules.</summary>
    public ServerIngressValueObjectValidator()
    {
        RuleFor(x => x.Scheme)
            .Must(scheme => scheme is "http" or "https")
            .WithMessage("'{PropertyName}' must be http or https.");
        RuleFor(x => x.Port!.Value).InclusiveBetween(1, 65535).When(x => x.Port is not null);
        RuleFor(x => x.EntryPoints).NotNull();
        RuleForEach(x => x.EntryPoints).MustMatch(InventoryPatternConstants.EntryPoint, "an entry point name");
        RuleFor(x => x.PrivateEntryPoints).NotNull();
        RuleForEach(x => x.PrivateEntryPoints).MustMatch(InventoryPatternConstants.EntryPoint, "an entry point name");
        RuleFor(x => x.CertResolver!)
            .MustMatch(InventoryPatternConstants.EntryPoint, "a certificate resolver name")
            .When(x => x.CertResolver is not null);
        RuleFor(x => x.Pattern!)
            .Must(pattern => pattern.Contains("{site}", StringComparison.Ordinal) && pattern.Length <= 200)
            .WithMessage("'{PropertyName}' must contain {site}, such as {site}-{product}.{environment}.preview.example.")
            .When(x => x.Pattern is not null);
        RuleFor(x => x.Probe!)
            .MustMatch(InventoryPatternConstants.Endpoint, "an address such as http://ingress:80")
            .When(x => x.Probe is not null);
        RuleFor(x => x.PrivateProbe!)
            .MustMatch(InventoryPatternConstants.Endpoint, "an address such as http://ingress:80")
            .When(x => x.PrivateProbe is not null);
    }
}
