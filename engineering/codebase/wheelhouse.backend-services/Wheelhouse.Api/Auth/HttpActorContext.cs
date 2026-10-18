using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Wheelhouse.Api.Auth;

/// <summary>Reads the actor from the request principal: the signed-in user's login, or <c>key:{name}</c> for an integration key.</summary>
public sealed class HttpActorContext(IHttpContextAccessor accessor) : IActorContext
{
    /// <inheritdoc />
    public string Actor
    {
        get
        {
            var user = accessor.HttpContext?.User;
            // A program acting through an integration key is named by the key, never passed off as a signed-in user.
            if (user?.GetApiKeyName() is { } key)
                return "key:" + key;
            return user?.Identity?.Name ?? user?.FindFirst("wt:username")?.Value ?? "authenticated-admin";
        }
    }
}
