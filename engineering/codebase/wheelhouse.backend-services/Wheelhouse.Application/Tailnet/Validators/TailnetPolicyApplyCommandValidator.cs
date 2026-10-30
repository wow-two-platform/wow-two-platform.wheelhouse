using FluentValidation;
using Wheelhouse.Application.Tailnet.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Tailnet.Validators;

/// <summary>Validates an apply: the review's ETag and digest.</summary>
public sealed class TailnetPolicyApplyCommandValidator : AbstractValidator<TailnetPolicyApplyCommand>
{
    /// <summary>Configures the rules.</summary>
    public TailnetPolicyApplyCommandValidator()
    {
        RuleFor(x => x.Etag).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Digest).Matches(FormatPatternConstants.Sha256Hex).WithMessage("'Digest' must be the reviewed policy's SHA-256.");
    }
}
