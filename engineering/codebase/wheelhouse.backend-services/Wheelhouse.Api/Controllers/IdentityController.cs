using System.Security.Claims;
using Wheelhouse.Api.Auth;
using Wheelhouse.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Claims;

namespace Wheelhouse.Api.Controllers;

/// <summary>Manages identity.</summary>
[ApiController]
[Route("api/identity")]
public sealed class IdentityController(IOptions<GitHubOAuthSettings> gitHub) : ControllerBase
{
    /// <summary>Begins sign-in.</summary>
    [AllowAnonymous]
    [HttpGet("sign-in")]
    public IActionResult SignIn([FromQuery] string? returnUrl)
    {
        // 503 until the OAuth app is configured — clearer than a generic 500 from an unregistered scheme.
        if (!gitHub.Value.IsConfigured)
            return Problem(
                detail: "GitHub sign-in is not configured. Set Identity:GitHub:ClientId and Identity:GitHub:ClientSecret (user-secrets).",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        // Local redirects only — never bounce the browser to an attacker-supplied absolute URL.
        var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        var properties = new AuthenticationProperties { RedirectUri = target };
        return Challenge(properties, AuthConfigurationExtensions.GitHubScheme);
    }

    /// <summary>Gets the current identity.</summary>
    [AllowAnonymous]
    [HttpGet("me")]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        return Ok(new CurrentUser(
            Login: User.GetUsername() ?? User.FindFirstValue(ClaimTypes.Name) ?? "",
            Name: User.GetDisplayName() ?? User.GetUsername() ?? "",
            Avatar: User.GetAvatar()));
    }

    /// <summary>Signs the caller out.</summary>
    [Authorize]
    [HttpPost("sign-out")]
    public new async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>The current signed-in admin, as surfaced to the dashboard.</summary>
    /// <param name="Login">GitHub login handle.</param>
    /// <param name="Name">Display name (falls back to the login).</param>
    /// <param name="Avatar">Avatar URL, if GitHub provided one.</param>
    public sealed record CurrentUser(string Login, string Name, string? Avatar);
}
