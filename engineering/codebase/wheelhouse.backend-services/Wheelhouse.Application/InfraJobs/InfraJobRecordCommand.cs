using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using Wheelhouse.Domain.Servers.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>Records a finished infrastructure job in the audit trail, as the owner who started it; an enrollment that
/// succeeded also points its server at the host's tailnet address and the deploy account, where prod's runner now
/// reaches it. A job that brought the host to the kit (enrollment, placement, host upkeep) records the ingress the kit
/// lays out and the server's public address.</summary>
public sealed record InfraJobRecordCommand : ICommand<AppResult<bool>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>The kind of job that enrolls a server.</summary>
    public const string Enrollment = "enrollment";

    /// <summary>The kind of job that places a product on a server.</summary>
    public const string Placement = "placement";

    /// <summary>The kind of job that brings an enrolled server to the current kit.</summary>
    public const string Upkeep = "upkeep";

    /// <summary>Gets the job's kind: <see cref="Enrollment"/>, <see cref="Placement"/> or <see cref="Upkeep"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the job's identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the server it ran on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets the product it placed, for a placement.</summary>
    public string? Product { get; init; }

    /// <summary>Gets how it ended: <c>succeeded</c> or <c>failed</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why it failed.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the tailnet address an enrollment joined the host at.</summary>
    public string? TailnetAddress { get; init; }

    /// <summary>Gets the single-use keys it minted, by ID, tag and expiry; never a key.</summary>
    public IReadOnlyList<string> Mints { get; init; } = [];

    /// <summary>Gets the ingress the kit laid out on the host, or <c>null</c> when the job did not reach it.</summary>
    public ServerIngressValueObject? Ingress { get; init; }

    /// <summary>Gets the server's public IPv4 address the job recorded, or <c>null</c>.</summary>
    public string? PublicAddress { get; init; }

    /// <summary>Gets the kit's digest on the host, for the audit detail.</summary>
    public string? KitDigest { get; init; }

    /// <inheritdoc />
    public string AuditAction => Kind switch
    {
        Enrollment => "server.enrollment.finish",
        Upkeep => "server.upkeep.finish",
        _ => "product.placement.finish",
    };

    /// <inheritdoc />
    public string AuditSubject => Kind == Placement ? Product ?? Server : Server;

    /// <inheritdoc />
    public string AuditDetail
    {
        get
        {
            var detail = "job " + (Id.Length >= 8 ? Id[..8] : Id) + (Kind == Placement ? " on " + Server : "")
                         + (TailnetAddress is null ? "" : ", tailnet " + TailnetAddress)
                         + (KitDigest is null ? "" : ", kit " + (KitDigest.Length > 12 ? KitDigest[..12] : KitDigest))
                         + (PublicAddress is null ? "" : ", public " + PublicAddress)
                         + (Mints.Count == 0 ? "" : "; keys " + string.Join(", ", Mints));
            return detail.Length > 480 ? detail[..480] : detail;
        }
    }
}
