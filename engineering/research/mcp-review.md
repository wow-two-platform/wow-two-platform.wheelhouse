# MCP review — network, authentication, reliability

*Last updated: 2026-10-17*

A read-only review of the `/mcp` endpoint against the code, the VPS setup and the MCP specification (2025-11-25 and
2026-07-28). Code and configuration were read; nothing ran against the host, so the live tailnet policy, the Serve
configuration and an authenticated client connection are unverified.

**Verdict:** sound for one operator on Claude Code. Network isolation, call auditing and limits come before any
write tool or secret.

## Network

| Finding | Severity | Evidence | Recommendation |
|---|---|---|---|
| One flat Docker network holds Postgres, both consoles, Traefik and the canary nodes and apps; any container on it reaches a console's `/mcp` without Tailscale (a key is still required) | medium | `deployment/vps/platform.compose.yml`, `vps/prepare.py` | Split into `control` (Traefik, consoles, Postgres) and `workloads` (Traefik plus product apps) |
| No tailnet policy is kept; Tailscale's default allows every node, so canary nodes reach :443, :8443 and :22 | medium, unverified | `platform.compose.yml` only advertises `tag:canary` | Grants: owner devices → consoles; CI tag → SSH; `tag:canary` → nothing. Test from a canary node |
| A compromised console reaches the host through its runner key on the deploy account | medium | `vps/authorize-runner.sh` | Keep tool arguments validated; later a forced-command account |
| No Origin check (the spec says MUST, 403); mitigated by allowed hosts, key-only auth with no cookies and no CORS | low | `Api/Mcp/WheelhouseMcpConfiguration.cs` | A small middleware: a foreign Origin gets 403 |
| `/mcp` inherits the 30 MB body limit | low | SDK request limits | About 64 KB on the endpoint |
| The canary entry point cannot reach the consoles | ok | Console routers match their host on the `private` and `dev` entry points only | Confirm with a `Host:` header from a canary node |
| TLS terminates at Serve; Traefik binds loopback; forwarded headers are trusted from known proxies only | ok | `platform.compose.yml` | Accept; tailnet node names appear in public certificate logs |

## Authentication

| Finding | Severity | Evidence | Recommendation |
|---|---|---|---|
| Keys never expire; rotation is create-then-revoke by hand | medium | No expiry on the key entity; lookup checks revocation only | Expiry (default 90 days) and a near-expiry signal |
| MCP calls are not audited; only last use is stored, per minute | medium | Only audited commands reach the trail; tools are queries | A call filter recording key, tool, target, outcome and duration; required before any write tool |
| `claude mcp add --header` stores the key in plain text in the local Claude config | medium | Claude Code MCP docs | Keep the key in the Keychain and pass it through a headers helper or an environment variable |
| Static bearer keys instead of MCP OAuth | low, acceptable | The spec makes authorization optional | Each key is minted by one instance and never forwarded. Move to short-lived OAuth for multiple users, cloud connectors, writes or secrets |
| `catalog:read` returns vault namespaces over REST but not over MCP | low | Scope constants vs `WheelhouseReadTools.cs` | One projection per scope |
| Key generation (~190 bits), hashed storage, uniform failures | ok | SDK API key factory and handler | — |
| Per-tool scope checks, immediate revocation, audited key creation, no keys in logs | ok | `WheelhouseReadTools.cs`, MCP E2E tests | — |

## Reliability

| Finding | Severity | Evidence | Recommendation |
|---|---|---|---|
| Every tool failure becomes one generic message, so an agent cannot tell a timeout from a down runner | medium | `WheelhouseReadTools.cs` vs `DeploymentGateway.cs` | Done 2026-10-17: `{ code, message, retryable }` tool errors |
| No rate or concurrency limit; each runner tool spawns Python and SSH for up to 110 s | medium | Rate limiting is off | A concurrency cap of 2–4 and a per-key rate limit on `/mcp` |
| Cloud connectors (claude.ai, ChatGPT) cannot reach a tailnet-only host | ok, by design | Provider docs | Use Claude Code or Codex on tailnet devices; never Funnel |
| With the database down, authentication fails first, so `get_health` never reports it | low | API key handler writes last use | Best-effort last-use write; `/health` for liveness |
| OAuth-probing clients get the SPA's HTML at `/.well-known/*` | low | SPA fallback | 404 for `/.well-known/*` |
| Stateless mode gives older clients no in-protocol confirmation | low, blocks writes | MCP C# SDK stateless remarks | Approval for writes happens outside the protocol |
| The 2026-07-28 protocol path is untested in Wheelhouse | low | E2E tests use 2025-11-25 | E2E coverage for discovery and the method header |
| Protocol versions, timeouts, cancellation, argument validation, no shell | ok | SDK MCP host, `DeploymentGateway.cs` | — |

## MCP and HTTP

- Keep both over the same mediator handlers: MCP is the agent adapter, REST serves scripts, CI and other services.
- Every MCP tool maps to a REST operation with the same scope and projection; no MCP-only features.
- A write-capable MCP needs its own short-lived scoped keys, the audited command handlers, the existing prod gates,
  idempotency handles and destructive annotations.
- Human approval for writes is enforced by the server: plan, approve in the console, then execute the plan.
- Secret-reading tools never go over MCP.

## Sources

[Transports 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports) ·
[Streamable HTTP 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http) ·
[Authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization) ·
[Security best practices](https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices) ·
[Claude Code MCP](https://code.claude.com/docs/en/mcp) ·
[Tailscale ACL samples](https://tailscale.com/kb/1192/acl-samples)
