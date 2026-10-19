using FluentValidation;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Tailnet.Commands;

namespace Wheelhouse.Application.Tailnet.Validators;

/// <summary>Validates an apply: the review's ETag and digest.</summary>
public sealed class TailnetPolicyApplyCommandValidator : AbstractValidator<TailnetPolicyApplyCommand>
{
    /// <summary>Configures the rules.</summary>
    public TailnetPolicyApplyCommandValidator()
    {
        RuleFor(x => x.Etag).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Digest).Matches(InfraPatterns.Digest).WithMessage("'Digest' must be the reviewed policy's SHA-256.");
    }
}
