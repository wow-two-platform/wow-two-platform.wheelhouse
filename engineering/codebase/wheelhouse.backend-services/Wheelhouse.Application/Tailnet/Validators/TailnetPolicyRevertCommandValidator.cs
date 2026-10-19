using FluentValidation;
using Wheelhouse.Application.Tailnet.Commands;

namespace Wheelhouse.Application.Tailnet.Validators;

/// <summary>Validates a revert: the live policy's ETag the owner saw.</summary>
public sealed class TailnetPolicyRevertCommandValidator : AbstractValidator<TailnetPolicyRevertCommand>
{
    /// <summary>Configures the rules.</summary>
    public TailnetPolicyRevertCommandValidator() => RuleFor(x => x.Etag).NotEmpty().MaximumLength(200);
}
