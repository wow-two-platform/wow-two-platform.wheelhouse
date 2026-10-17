using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors.Mappers;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;

namespace Wheelhouse.Api.Mcp;

/// <summary>Returns a failed tool call as an MCP tool error — <c>isError</c> with one text block holding a
/// <see cref="McpToolError"/> as JSON — instead of the transport's one generic message.</summary>
/// <remarks>
/// A tool fails by throwing its <see cref="AppError"/> as an <see cref="AppException"/>, which keeps its own message.
/// Any other exception is logged and answered with the generic message of the kind it maps to, so exception text
/// (stack traces, paths, hosts, values) never reaches the agent. Cancellation and protocol errors, such as a refused
/// scope, pass through to the transport.
/// </remarks>
public static partial class McpToolErrorFilter
{
    /// <summary>Wraps the tool-call pipeline.</summary>
    /// <param name="next">The next handler: the remaining filters and the tool.</param>
    /// <returns>The wrapped handler.</returns>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Apply(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            try
            {
                return await next(request, ct);
            }
            catch (Exception exception) when (exception is not McpProtocolException
                && !(exception is OperationCanceledException && ct.IsCancellationRequested))
            {
                return Describe(request, exception);
            }
        };

    private static CallToolResult Describe(RequestContext<CallToolRequestParams> request, Exception exception)
    {
        var services = request.Services;
        var natures = services?.GetService<IErrorNatureMapper>() ?? new ErrorNatureMapper();
        McpToolError error;
        if (exception is AppException application)
        {
            error = McpToolError.From(application.Error, natures);
        }
        else
        {
            if (services?.GetService<ILoggerFactory>() is { } loggers)
                ToolFailed(loggers.CreateLogger(typeof(McpToolErrorFilter)), exception, request.Params?.Name);
            var kind = (services?.GetService<IExceptionMapper>() ?? new ExceptionMapper([])).Map(exception).Type;
            error = McpToolError.Generic(kind, natures);
        }

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(error, JsonOptionsConstants.Default) }],
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP tool {Tool} failed unexpectedly.")]
    private static partial void ToolFailed(ILogger logger, Exception exception, string? tool);
}
