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
  - `GET /api/audit?pageSize=&pageToken=&product=` answers `{ items, pageSize, nextPageToken }`, newest first by the
    unique sequence number (EF `ToTokenPageAsync`, token key `long`); `pageSize` 1-200 (default 50), a malformed token
    or an out-of-range size answers 400; the API, console and E2E tests send no `before`; no MCP tool reads the list
  - Activity page and a product's Activity tab: `useAppInfiniteQuery` over the token, one Load older actions button
    replaces the Newer / Older windows; the area filter still narrows the loaded entries, verification unchanged
- Add the shared-ref hook to `wow-two-sdk-beta.fe`, release it, re-pin `@wow-two-beta/fe`, then move the preferences;
  read the old raw values once so no owner preference is lost
  - fe `0.0.35` `createPersistentRef(key, initial, { decode })`: one module-scope ref per choice, a decoder each, tab
    sync over `localStorage`
  - `adoptRawPreferences` re-encodes a non-JSON value (`dev`, `rows`) as JSON at load and drops an empty one; JSON
    values (`true`, lists, the last product) stay as they were. Removable once the owner's browsers have loaded v0.10
