# v0.10 Iteration 1 — Identity blocks (transient plan)

*Last updated: 2026-10-30*

Transient: deleted when Iteration 1 closes.

## What

| Task | Wheelhouse source | Note from the sweep |
|---|---|---|
| Session cookie name per environment | `Wheelhouse.Infrastructure/Settings/SessionCookies.cs` | Dev and prod share one host name, so each needs its own cookie; the identity sdk's `CookieAuthOptions.CookieName` takes a plain name |
| OAuth sign-in, me and sign-out endpoints | `Wheelhouse.Api/Controllers/IdentityController.cs` | Registration only when sign-in is configured |

## How

- Build in `wow-two-sdk-beta.identity`, release through its CI, then re-pin `IdentitySdkVersion` in
  `Directory.Packages.props` and delete the Wheelhouse copies
- Dev and prod must keep distinct cookies after the move; the E2E sign-in tests stay green

## Built

| Block | Identity sdk (Auth / Providers) | Wheelhouse wiring |
|---|---|---|
| Cookie name | `CookieAuthOptions.Environment` + `PrimaryEnvironment` (`prod`); `EnvironmentCookieName.For` names the cookie | `AuthConfigurationExtensions`: `CookieName = SessionCookie`, `Environment = GetDeployEnvironment()` |
| Endpoints | `MapOAuthSignInEndpoints(scheme, o => o.NotConfiguredDetail = …)` in `OAuth/Endpoints/`: minimal APIs, 503 while the scheme is unregistered | `MapIdentityEndpoints()` mounts them on `/api/identity` after `MapControllers()` |

- "Registered only when configured" stays the GitHub scheme: the endpoints always map, and sign-in answers 503 until
  the scheme exists, as the controller did; the `IOptions<GitHubOAuthSettings>` registration it read is gone
- `me` 401 and the 503 are `Results.Problem`, so the bodies keep MVC's `type` and `title`
- Proof: `IdentityE2ETests` pins `me`, `sign-out`, `sign-in` and the dev / prod cookie names; responses diffed
  identical, headers and bodies, before and after the switch
- Released as identity `10.0.9-beta` (`be-v10.0.9-beta`); its first run failed restore on NU1902 (MySqlConnector
  2.3.5, GHSA-473q-m89c-ghf8), fixed by moving the identity sdk's core pin to be sdk `10.0.94-beta`
