using FluentValidation;
using Wheelhouse.Application.Deployments.Freezes.Validators;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Application.SecretsVault.Validators;

/// <summary>Validates marking the offline copy verified: a fingerprint as the console shows it, and a one-line reason of
/// 300 characters at most.</summary>
public sealed class RootKeyOfflineCopyVerifyCommandValidator : AbstractValidator<RootKeyOfflineCopyVerifyCommand>
{
    /// <summary>Configures the rules.</summary>
    public RootKeyOfflineCopyVerifyCommandValidator()
    {
        RuleFor(x => x.Fingerprint).Matches(KeyFingerprintHasher.Pattern)
            .WithMessage("'Fingerprint' must be the key in use's fingerprint as the console shows it.");
        RuleFor(x => x.Reason).Must(reason => reason is { Length: > 0 } && reason.Trim().Length > 0 && FreezeRules.IsReason(reason))
            .WithMessage("'Reason' must say how you checked the copy, in one line of 300 characters at most.");
    }
}
