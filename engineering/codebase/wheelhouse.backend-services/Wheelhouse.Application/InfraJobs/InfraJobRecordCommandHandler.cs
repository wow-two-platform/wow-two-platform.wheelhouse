using System.Net;
using System.Net.Sockets;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Validators;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>Handles <see cref="InfraJobRecordCommand"/>. A failed job is recorded as a failed action with its reason. A
/// job that brought the host to the kit sets the server's ingress to what the kit lays out, keeping the owner's host
/// pattern, and records the public address it found; a job that found none keeps the one recorded before.</summary>
public sealed class InfraJobRecordCommandHandler(IServersRepository servers)
    : ICommandHandler<InfraJobRecordCommand, AppResult<bool>>
{
    /// <summary>The account prod's runner signs in as on every products host.</summary>
    public const string DeployUser = "wheelhouse-deploy";

    /// <inheritdoc />
    public async ValueTask<AppResult<bool>> HandleAsync(InfraJobRecordCommand request, CancellationToken cancellationToken)
    {
        if (request.Status != "succeeded")
            return AppResult<bool>.Fail(AppErrorFactory.ExternalUnavailable(request.Reason ?? "The job failed."));
        var enrollment = request.Kind == InfraJobRecordCommand.Enrollment;
        if (enrollment && !InTailnet(request.TailnetAddress))
            return AppResult<bool>.Fail(AppErrorFactory.Validation("The enrollment recorded no tailnet address."));
        if (!enrollment && request.Ingress is null)
            return AppResult<bool>.Ok(true);
        var server = await servers.GetBySlugAsync(request.Server, cancellationToken);
        if (server is null)
            return AppResult<bool>.Fail(AppErrorFactory.NotFound($"Server '{request.Server}' was not found."));
        if (enrollment)
        {
            server.Host = request.TailnetAddress!;
            server.SshUser = DeployUser;
            server.SshPort = 22;
        }

        if (request.Ingress is { } ingress)
        {
            var recorded = ingress with
            {
                Pattern = server.Ingress.Pattern,
                PublicAddress = request.PublicAddress ?? server.Ingress.PublicAddress,
            };
            if (!(await new ServerIngressValueObjectValidator().ValidateAsync(recorded, cancellationToken)).IsValid)
                return AppResult<bool>.Fail(AppErrorFactory.Validation("The job recorded an ingress Wheelhouse cannot use."));
            server.Ingress = recorded;
        }

        await servers.UpdateAsync(server, cancellationToken);
        return AppResult<bool>.Ok(true);
    }

    private static bool InTailnet(string? address)
    {
        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var bytes = parsed.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
