# Wheelhouse MCP

The read-only MCP implementation is adopted with the published be sdk `10.0.64-beta`.
It exposes stateless Streamable HTTP at `/mcp` on the private API host. The rebuilt local API is running;
an authenticated external client connection remains manual acceptance.

## Connection

- Endpoint: `https://<private-wheelhouse-host>/mcp`.
- Local HTTPS API: `https://localhost:8210/mcp`.
- Authenticate with `Authorization: Bearer <integration-key>` on every request.
- Create the key in **Settings → Integration keys** using the owner's signed-in session.
- Keep the key in the MCP client's secret or environment storage; never commit it.
- Keep the endpoint on Tailscale or an SSH tunnel, matching the control plane's private-network policy.
- Browser login cookies do not authenticate the MCP endpoint.
- Use a client supporting Streamable HTTP and a custom Bearer credential; OAuth discovery is not implemented, and
  every `/.well-known/*` path answers 404.
- A request carrying an `Origin` header gets 403 unless it is the console's own origin or listed in
  `Mcp:AllowedOrigins` (`scheme://host[:port]`, empty by default). CLI clients send no `Origin` and pass.
- Request bodies over 64 KiB get 413; an `initialize` or a tool call takes a few KiB at most.
- Both checks run before authentication, in the product's `McpRequestGuard`.

## Scopes and tools

| Scope | Tools |
|---|---|
| `catalog:read` | `list_projects` |
| `deployments:read` | `list_servers`, `list_targets`, `list_deployments`, `get_deployment_state`, `check_target`, `get_vitals`, `get_health` |

- `list_projects` returns what `GET /api/products` returns to the same key: id, identity, workspace id, status and
  inactive service keys, repository, release source, environment sites and each environment's vault and namespace.
  Secret values are never returned. The five-value `lifecycle` was replaced by `status` (`active`/`inactive`) in v0.5.
- `list_targets` omits settings and credential paths.
- `list_deployments` names each deployment's `actor`, whoever submitted it: a signed-in user's login, or `key:{name}`
  for an integration key — the word the console and the REST API use too.
- Target-specific tools require an existing lowercase target ID from `list_targets`.
- `get_health` reports control-plane database readiness, build identity and the local-rehearsal marker.
- Fleet reads reuse the existing mediator handlers and runner; a queued result is not deployment success.
- Discovery lists only tools authorized by the supplied key, and direct unauthorized calls are refused.
- Revoking a key takes effect on its next request; no MCP authentication session is retained.
- No tools build, deploy, reconcile, read logs, expose secrets or modify inventory.

## Errors

- A failed call returns `isError: true` with one text block: `{"code": "…", "message": "…", "retryable": …}`.
- `code` is the error kind REST names in a problem's `code`; `message` is the display-safe text the console shows.
- Retryable: `OperationTimeout` (the runner timed out) and `ExternalUnavailable` (runner unreachable, SSH or step failure).
- Not retryable: `Validation`, `NotFound`, `Conflict` (the runner refused), `Unexpected`.
- An unexpected exception answers its kind's generic message; exception text never reaches the client.
- A key lacking the tool's scope gets a JSON-RPC error instead of a tool error.

## Delivery status

The implementation uses the SDK `Ai/Mcp` host helpers. The be sdk MCP host has no Origin check or endpoint body
limit, and its SPA fallback reserves only `/api`, so the guard and the `/.well-known/*` 404 are product-local until
the SDK provides them. Every be sdk family reference now uses
the publicly verified `10.0.67-beta` release. No production deployment is established here.

## Adoption verification — 2026-10-06

- All seven SDK release packages were verified as publicly available before changing the product pin.
- Normal-source build with the production frontend passed with zero warnings or errors.
- All 223 backend tests passed, including MCP protocol, key scopes, revocation and PostgreSQL checks.
- The rebuilt local API returns `401` to anonymous MCP initialization POST requests.
- A real GitHub sign-in and an authenticated external MCP client remain manual acceptance.

## Local verification — 2026-10-05

- SDK source: isolated `HEAD` plus only the MCP/AI lane changes; unrelated staged changes excluded.
- Local package: `WoW2.Sdk.Backend.Beta` (since renamed `WoW2.Sdk.Beta.Be`) `10.0.64-mcp.20261005.2`, with its matching data-abstractions package.
- SDK checks: 6 passed, covering both `2025-11-25` and `2026-07-28`, scopes, statelessness and exception redaction.
- Wheelhouse checks: 136 E2E tests passed against that package and the existing built frontend assets.
- Verification copy: `/private/tmp/wheelhouse-mcp-verification`; package files: `/private/tmp/wheelhouse-mcp-packages`.
- These checks preceded published-package adoption; they do not establish the current runtime state.

Transport and protocol behavior: [official MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/v2/).
