// The local rig's owner session: a short-lived sign-in cookie for the rig's own console, sealed with that console's
// Data Protection key ring (the `console-keys` volume), so a rig action drives the console's API as the allowlisted
// owner without a GitHub OAuth app or a browser. It holds the claims a GitHub sign-in leaves after normalization, and
// nothing a deployed console would accept: only the rig's key ring opens it. Usage: <key folder> <login> <minutes>.
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 3 || !int.TryParse(args[2], out var minutes) || minutes is < 1 or > 120)
{
    Console.Error.WriteLine("usage: Session.dll <key folder> <login> <minutes, 1-120>");
    return 2;
}

// As the console registers it (HostConfigurationExtensions.AddSettings): the application name isolates its keys.
var services = new ServiceCollection();
services.AddDataProtection().SetApplicationName("Wheelhouse").PersistKeysToFileSystem(new DirectoryInfo(args[0]));
using var provider = services.BuildServiceProvider();
// The purposes the cookie handler protects its tickets with, for the default "Cookies" scheme.
var protector = provider.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Cookies", "v2");
var identity = new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, "local-rig"),
        new Claim(ClaimTypes.Name, args[1]),
        new Claim("wt:user_id", "local-rig"),
        new Claim("wt:username", args[1]),
    ],
    "Cookies");
var now = DateTimeOffset.UtcNow;
var ticket = new AuthenticationTicket(
    new ClaimsPrincipal(identity),
    new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddMinutes(minutes) },
    "Cookies");
Console.WriteLine(new TicketDataFormat(protector).Protect(ticket));
return 0;
