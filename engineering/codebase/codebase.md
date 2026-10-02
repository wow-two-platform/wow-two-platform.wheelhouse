# codebase/

*Last updated: 2026-10-02*

The code — the only place code lives.

| Dir | What |
|---|---|
| `wheelhouse.backend-services/` | .NET 10 Clean-Arch solution (`Wheelhouse.BackendServices.slnx`) — `Wheelhouse.{Api,Application,Domain,Infrastructure,Persistence}` plus four test tiers; central package versions and the SDK pin beside the solution |
| `wheelhouse.frontend-services/` | Private pnpm workspace; `apps/web/` (`@wheelhouse/web`) owns the Vue workspace, its tests and Vite output |
| `wheelhouse.runner-services/` | Python runner — inventory snapshot reader, release catalog, SSH transport and target executor |

Build / run: root `README.md`. Architecture: `../architecture/architecture.md`.
