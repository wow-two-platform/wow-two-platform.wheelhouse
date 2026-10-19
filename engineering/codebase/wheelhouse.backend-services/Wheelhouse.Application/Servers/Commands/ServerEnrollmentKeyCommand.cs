using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Servers.Commands;

/// <summary>Creates a server's one-time enrollment key, once; its private half never leaves the control host.</summary>
/// <param name="Slug">The server's slug.</param>
public sealed record ServerEnrollmentKeyCommand(string Slug) : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "server.enrollment.key";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => null;
}
