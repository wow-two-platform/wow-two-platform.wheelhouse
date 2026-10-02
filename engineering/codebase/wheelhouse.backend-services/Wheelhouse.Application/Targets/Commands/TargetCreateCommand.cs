using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.Commands;

/// <summary>Represents a command to add an environment of a product on a server.</summary>
public sealed record TargetCreateCommand : ICommand<AppResult<TargetDto>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the name deployments and the runner will know the target by, such as <c>foreverpin-prod</c>; fixed once created.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the slug of the product the target runs.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the slug of the server the target runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets which stage of the product the target runs.</summary>
    public required DeploymentEnvironment Environment { get; init; }

    /// <summary>Gets the Docker network the target's services share with the host's platform services.</summary>
    public required string Network { get; init; }

    /// <summary>Gets the folder on the host the runner keeps the target's releases under.</summary>
    public required string Root { get; init; }

    /// <summary>Gets each service's settings file on the host.</summary>
    public required IReadOnlyList<TargetSettingValueObject> Settings { get; init; }

    /// <summary>Gets the requests that prove a rollout.</summary>
    public required IReadOnlyList<SmokeCheckValueObject> SmokeChecks { get; init; }

    /// <summary>Gets the hosts of sites the server's pattern does not cover.</summary>
    public required IReadOnlyList<SiteHostValueObject> Sites { get; init; }

    /// <inheritdoc />
    public string AuditAction => "target.create";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => Product + " " + Environment.ToString().ToLowerInvariant() + " on " + Server;
}
