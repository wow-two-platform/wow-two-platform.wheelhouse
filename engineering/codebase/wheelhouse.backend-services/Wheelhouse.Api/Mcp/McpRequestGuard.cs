using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Api.Mcp;

/// <summary>Screens requests to the MCP endpoint before authentication: a foreign browser origin gets 403 and a body
/// over <see cref="MaxBodyBytes"/> gets 413.</summary>
/// <remarks>
/// The origin check is the DNS-rebinding defence the Streamable HTTP transport requires: a request carrying an
/// <c>Origin</c> other than the console's own or one in <see cref="McpSettings.AllowedOrigins"/> is refused, and a
/// request without one, as a CLI client sends, passes. The body is read into memory up to the limit, so a chunked or
/// compressed body is bounded too, not only a declared <c>Content-Length</c>.
/// </remarks>
public sealed class McpRequestGuard(RequestDelegate next)
{
    /// <summary>Holds the largest request body the endpoint reads, 64 KiB. The largest legitimate request, an
    /// <c>initialize</c> with the client's capabilities or a tool call with one target slug, stays within a few KiB.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>Screens one request.</summary>
    /// <param name="context">The request.</param>
    /// <param name="settings">The allowed browser origins.</param>
    /// <returns>A task completing when the request is answered or passed on.</returns>
    public async Task InvokeAsync(HttpContext context, McpSettings settings)
    {
        if (!context.Request.Path.StartsWithSegments(WheelhouseMcpConfiguration.Path))
        {
            await next(context);
            return;
        }

        var origin = context.Request.Headers.Origin;
        if (origin.Count > 0 && !settings.Allows(origin.ToString(), OwnOrigin(context.Request)))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                detail: "This origin may not call the Wheelhouse MCP endpoint.").ExecuteAsync(context);
            return;
        }

        if (!await BufferBodyAsync(context))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                detail: "The request body exceeds the MCP endpoint's 64 KiB limit.").ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    // Forwarded headers have already applied, so this is the address the client used, such as the tailnet host name.
    private static string OwnOrigin(HttpRequest request) => request.Scheme + "://" + request.Host.ToUriComponent();

    private static async Task<bool> BufferBodyAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.ContentLength > MaxBodyBytes)
            return false;

        var body = new MemoryStream();
        context.Response.RegisterForDispose(body);
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (body.Length + read > MaxBodyBytes)
                return false;
            body.Write(chunk, 0, read);
        }

        body.Position = 0;
        request.Body = body;
        return true;
    }
}
