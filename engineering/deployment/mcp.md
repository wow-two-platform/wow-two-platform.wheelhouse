# Wheelhouse MCP

The read-only MCP implementation is adopted with the published backend SDK `10.0.64-beta`.
It exposes stateless Streamable HTTP at `/mcp` on the private API host. The rebuilt local API is running;
an authenticated external client connection remains manual acceptance.

## Connection

- Endpoint: `https://<private-wheelhouse-host>/mcp`.
- Local HTTPS API: `https://localhost:8210/mcp`.
- Authenticate with `Authorization: Bearer <integration-key>` on every request.
- Create the key in **Settings → Integration keys** using the operator's signed-in session.
- Keep the key in the MCP client's secret or environment storage; never commit it.
- Keep the endpoint on Tailscale or an SSH tunnel, matching the control plane's private-network policy.
- Browser login cookies do not authenticate the MCP endpoint.
- Use a client supporting Streamable HTTP and a custom Bearer credential; OAuth discovery is not implemented.

## Scopes and tools

| Scope | Tools |
|---|---|
| `catalog:read` | `list_projects` |
| `deployments:read` | `list_servers`, `list_targets`, `list_deployments`, `get_deployment_state`, `check_target`, `get_vitals`, `get_health` |

- `list_projects` maps Wheelhouse products to project metadata; secret namespaces are omitted.
- `list_targets` omits settings and credential paths.
- Target-specific tools require an existing lowercase target ID from `list_targets`.
- `get_health` reports control-plane database readiness, build identity and the local-rehearsal marker.
- Fleet reads reuse the existing mediator handlers and runner; a queued result is not deployment success.
- Discovery lists only tools authorized by the supplied key, and direct unauthorized calls are refused.
- Revoking a key takes effect on its next request; no MCP authentication session is retained.
- No tools build, deploy, reconcile, read logs, expose secrets or modify inventory.

## Delivery status

The implementation uses the SDK `Ai/Mcp` host helpers. Every backend SDK family reference now uses
the publicly verified `10.0.64-beta` release. No production deployment is established here.

## Adoption verification — 2026-10-06

- All seven SDK release packages were verified as publicly available before changing the product pin.
- Normal-source build with the production frontend passed with zero warnings or errors.
- All 223 backend tests passed, including MCP protocol, key scopes, revocation and PostgreSQL checks.
- The rebuilt local API returns `401` to anonymous MCP initialization POST requests.
- Real operator OAuth and an authenticated external MCP client remain manual acceptance.

## Local verification — 2026-10-05

- SDK source: isolated `HEAD` plus only the MCP/AI lane changes; unrelated staged changes excluded.
- Local package: `WoW2.Sdk.Backend.Beta` `10.0.64-mcp.20261005.2`, with its matching data-abstractions package.
- SDK checks: 6 passed, covering both `2025-11-25` and `2026-07-28`, scopes, statelessness and exception redaction.
- Wheelhouse checks: 136 E2E tests passed against that package and the existing built frontend assets.
- Verification copy: `/private/tmp/wheelhouse-mcp-verification`; package files: `/private/tmp/wheelhouse-mcp-packages`.
- These checks preceded published-package adoption; they do not establish the current runtime state.

Transport and protocol behavior: [official MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/v2/).
