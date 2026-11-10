using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Validators;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>Handles <see cref="InfraJobRecordCommand"/>. A failed job is recorded as a failed action with its reason. A
/// job that brought the host to the kit sets the server's ingress to what the kit lays out, keeping the owner's host
/// pattern, and records the public address it found; a job that found none keeps the one recorded before. A placement
/// gives each private site it put on the tailnet its node's host on the environment that node serves, on that server,
/// unless the environment already stores a host for the site: without one, the ingress never routes the site.</summary>
public sealed class InfraJobRecordCommandHandler(IServersRepository servers, IProductsRepository products, ITargetsRepository targets)
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
        if (request.Kind == InfraJobRecordCommand.Placement && request.Product is not null && request.Sites.Count > 0)
            await RecordSitesAsync(request, cancellationToken);
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

    /// <summary>Gives each placed private site its node's host on the product's environment on the job's server, where
    /// the environment stores no host for that site; a host that is no lowercase DNS name is left out. @internal</summary>
    private async Task RecordSitesAsync(InfraJobRecordCommand request, CancellationToken ct)
    {
        var product = await products.GetBySlugAsync(request.Product!, ct);
        var server = await servers.GetBySlugAsync(request.Server, ct);
        if (product is null || server is null)
            return;
        var host = new Regex(FormatPatternConstants.DomainName);
        foreach (var environment in request.Sites.GroupBy(site => site.Environment, StringComparer.Ordinal))
        {
            if (!Enum.TryParse<DeploymentEnvironment>(environment.Key, ignoreCase: true, out var parsed))
                continue;
            var slug = (await targets.GetAllAsync(ct))
                .FirstOrDefault(target => target.ProductId == product.Id && target.ServerId == server.Id && target.Environment == parsed)?.Slug;
            if (slug is null || await targets.GetBySlugAsync(slug, ct) is not { } target)
                continue;
            var sites = target.Sites.ToList();
            var changed = false;
            foreach (var site in environment.Where(site => host.IsMatch(site.Host)))
            {
                if (sites.Any(stored => stored.Site == site.Site && !string.IsNullOrWhiteSpace(stored.Host)))
                    continue;
                sites.RemoveAll(stored => stored.Site == site.Site);
                sites.Add(new SiteHostValueObject { Site = site.Site, Host = site.Host });
                changed = true;
            }

            if (!changed)
                continue;
            target.Sites = sites;
            await targets.UpdateAsync(target, ct);
        }
    }

    private static bool InTailnet(string? address)
    {
        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var bytes = parsed.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
