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
