using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Wheelhouse.Api.Auth;

/// <summary>Reads the actor from the request principal: the signed-in operator, or <c>key:{name}</c> for an integration key.</summary>
public sealed class HttpOperatorContext(IHttpContextAccessor accessor) : IOperatorContext
{
    /// <inheritdoc />
    public string Actor
    {
        get
        {
            var user = accessor.HttpContext?.User;
            // A program acting through an integration key is named by the key, never passed off as the operator.
            if (user?.GetApiKeyName() is { } key)
                return "key:" + key;
            return user?.Identity?.Name ?? user?.FindFirst("wt:username")?.Value ?? "authenticated-admin";
        }
    }
}
