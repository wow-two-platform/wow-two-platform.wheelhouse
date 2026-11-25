using FluentValidation;

namespace Wheelhouse.Application.SystemStatus.Models;

/// <summary>Requires version and environment metadata in the console's status.</summary>
public sealed class SystemStatusModelValidator : AbstractValidator<SystemStatusModel>
{
    public SystemStatusModelValidator()
    {
        RuleFor(status => status.Version).NotEmpty();
        RuleFor(status => status.Environment).NotEmpty();
    }
}
