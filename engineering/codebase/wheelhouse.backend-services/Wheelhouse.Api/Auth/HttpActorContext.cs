using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Wheelhouse.Api.Auth;

/// <summary>Reads the actor from the request principal: the signed-in user's login, or <c>key:{name}</c> for an integration
/// key. Work outside any request acts for the <see cref="BackgroundActor"/> its scope names — the owner whose fleet
/// rollout a pass continues — and otherwise, as the automation passes do, as <c>automation</c>.</summary>
public sealed class HttpActorContext(IHttpContextAccessor accessor, BackgroundActor background) : IActorContext
{
    /// <inheritdoc />
    public string Actor
    {
        get
        {
            if (accessor.HttpContext is not { } context)
                return background.Actor ?? Wheelhouse.Application.Deployments.Automation.Constants.AutomationConstants.Actor;
            var user = context.User;
            // A program acting through an integration key is named by the key, never passed off as a signed-in user.
            if (user.GetApiKeyName() is { } key)
                return "key:" + key;
            return user.Identity?.Name ?? user.FindFirst("wt:username")?.Value ?? "authenticated-admin";
        }
    }
}
