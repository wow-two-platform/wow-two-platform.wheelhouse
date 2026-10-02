using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Wheelhouse.Api.Filters;

/// <summary>Refuses a write whose <c>X-Wheelhouse-Action</c> header does not name it, before the body binds into a
/// command. A non-simple custom header blocks cross-origin cookie writes without trusting a body token.</summary>
/// <param name="action">The action the header must name, such as <c>deploy</c>.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireActionAttribute(string action) : Attribute, IResourceFilter
{
    /// <summary>Holds the header every operator write carries.</summary>
    public const string HeaderName = "X-Wheelhouse-Action";

    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.Headers[HeaderName] == action)
            return;
        var problems = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        context.Result = new ObjectResult(problems.CreateProblemDetails(
            context.HttpContext, StatusCodes.Status400BadRequest, detail: $"An explicit {action} action is required."))
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
