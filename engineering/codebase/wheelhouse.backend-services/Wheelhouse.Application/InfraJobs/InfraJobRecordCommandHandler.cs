using System.Net;
using System.Net.Sockets;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>Handles <see cref="InfraJobRecordCommand"/>. A failed job is recorded as a failed action with its reason.</summary>
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
        if (request.Kind != InfraJobRecordCommand.Enrollment)
            return AppResult<bool>.Ok(true);
        if (!InTailnet(request.TailnetAddress))
            return AppResult<bool>.Fail(AppErrorFactory.Validation("The enrollment recorded no tailnet address."));
        var server = await servers.GetBySlugAsync(request.Server, cancellationToken);
        if (server is null)
            return AppResult<bool>.Fail(AppErrorFactory.NotFound($"Server '{request.Server}' was not found."));
        server.Host = request.TailnetAddress!;
        server.SshUser = DeployUser;
        server.SshPort = 22;
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
