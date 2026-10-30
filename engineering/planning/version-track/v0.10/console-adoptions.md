# v0.10 Iteration 3 — Console adoptions (transient plan)

*Last updated: 2026-10-30*

Transient: deleted when Iteration 3 closes.

## What

| Task | Wheelhouse source | Note from the sweep |
|---|---|---|
| SDK token paging in the audit list | the audit list query and endpoint, the Activity page | Replaces the audit list's `before`; a wire change with the Activity page, shipped together |
| Persisted preferences on the fe sdk storage broker | `apps/web/src/application/preferences` | The broker JSON-encodes, so raw values stored today read once as null; shared refs with decoders need an fe sdk hook |

## How

- Token paging uses the be sdk's `TokenPageDto` / `TokenPageApiRequest`; no SDK change expected
- Add the shared-ref hook to `wow-two-sdk-beta.fe`, release it, re-pin `@wow-two-beta/fe`, then move the preferences;
  read the old raw values once so no owner preference is lost
