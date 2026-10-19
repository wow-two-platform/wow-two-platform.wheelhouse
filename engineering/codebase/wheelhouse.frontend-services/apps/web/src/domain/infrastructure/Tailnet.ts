import { NetworkAddress } from './NetworkAddress';

/** What a tailnet node is to Wheelhouse. */
export const TailnetNodeRole = {
  /** A products host a server enrolled. */
  Host: 'host',
  /** A product site's own node. */
  Site: 'site',
  /** The control host Wheelhouse runs on. */
  Control: 'control',
  Other: 'other',
} as const;
export type TailnetNodeRole = (typeof TailnetNodeRole)[keyof typeof TailnetNodeRole];

/** One device on the tailnet, as the Tailscale devices read reports it. */
export interface TailnetNode {
  id: string;
  name: string;
  hostname: string;
  addresses: string[];
  tags: string[];
  online: boolean;
  lastSeen: string | null;
  keyExpiryDisabled: boolean;
  expires: string | null;
  created: string | null;
  os: string | null;
  authorized: boolean | null;
  /** The server this node belongs to, or null for site nodes and the control host. */
  serverId: string | null;
  role: TailnetNodeRole;
}

/** Whether Wheelhouse reaches Tailscale, and the nodes it reads there. */
export interface TailnetStatus {
  /** The OAuth client file is in place. */
  configured: boolean;
  /** Tailscale accepted the client and listed devices. */
  connected: boolean;
  /** Deployed dev, which never connects: Tailscale and enrollment are prod-only. */
  prodOnly: boolean;
  /** Why it is not configured or connected, as display text. */
  reason: string | null;
  /** The tailnet's domain, such as `tail1234.ts.net`. */
  tailnet: string | null;
  /** The OAuth client's path relative to the runner root. */
  clientFile: string;
  nodes: TailnetNode[];
  servers: Array<{ serverId: string; nodeId: string | null }>;
}

/** One line of the policy diff: unchanged, added or removed, with its old and new line numbers. */
export interface PolicyDiffLine {
  kind: 'same' | 'add' | 'remove';
  old: number | null;
  new: number | null;
  text: string;
}

/** One hunk of the policy diff, with a few unchanged lines around its changes. */
export interface PolicyDiffHunk {
  oldStart: number;
  newStart: number;
  lines: PolicyDiffLine[];
}

/** The live tailnet policy beside the one Wheelhouse renders from its inventory; reading it changes nothing. */
export interface PolicyReview {
  /** The live policy's version; an apply sends it back, so a policy changed since is refused. */
  etag: string | null;
  live: string;
  rendered: string;
  /** The rendered policy's SHA-256, hex; an apply sends it back, so only what was reviewed is written. */
  renderedDigest: string;
  changed: boolean;
  diff: PolicyDiffHunk[];
  /** The products hosts the inventory adds. */
  hosts: Array<{ serverId: string; address: string }>;
  /** Tailscale's own validation of the rendered policy, its tests included. */
  validation: { ok: boolean; problems: string[] };
  /** Wheelhouse's control-path guard: the rendered policy still lets the control host reach products hosts. */
  guard: { ok: boolean; reason: string | null };
  /** The policy live before the last apply, which one click puts back. */
  previous: { savedAt: string | null; actor: string | null; digest: string | null } | null;
}

/** An applied policy. */
export interface PolicyApplied {
  applied: true;
  digest: string;
  etag: string | null;
  appliedAt: string;
}

/** A reverted policy. */
export interface PolicyReverted {
  reverted: true;
  digest: string;
  etag: string | null;
  revertedAt: string;
}

/** The OAuth client's scopes. */
export const TAILNET_SCOPES = ['auth_keys', 'devices:core:read', 'policy_file'] as const;
/** The tag the OAuth client owns. */
export const TAILNET_CLIENT_TAG = 'tag:wheelhouse';

/** Where the Tailnet panel stands. */
export const TailnetConnection = {
  Connected: 'connected',
  /** Deployed dev: never connects. */
  ProdOnly: 'prod-only',
  /** No OAuth client in place: the owner's one-time step. */
  NotConfigured: 'not-configured',
  /** The client is in place but Tailscale refused it or did not answer. */
  Refused: 'refused',
} as const;
export type TailnetConnection = (typeof TailnetConnection)[keyof typeof TailnetConnection];

/** Pure rules over the tailnet status and its policy review. */
export const TailnetRules = {
  /** Where the panel stands. */
  connectionOf: (status: TailnetStatus): TailnetConnection =>
    status.prodOnly
      ? TailnetConnection.ProdOnly
      : status.connected
        ? TailnetConnection.Connected
        : status.configured
          ? TailnetConnection.Refused
          : TailnetConnection.NotConfigured,

  /** The OAuth client's place on the control host. */
  clientPath: (status: Pick<TailnetStatus, 'clientFile'>): string =>
    `<runner root>/${status.clientFile.replace(/^\/+/, '')}`,

  /** The server's node, matched by the server it belongs to. */
  nodeOf: (status: TailnetStatus | null | undefined, serverId: string): TailnetNode | null =>
    status?.nodes.find((node) => node.serverId === serverId) ?? null,

  /** The node's tailnet IPv4 address, else its first address. */
  addressOf: (node: Pick<TailnetNode, 'addresses'>): string | null =>
    node.addresses.find((address) => NetworkAddress.isTailnet(address)) ?? node.addresses[0] ?? null,

  /** Whether the reviewed policy may be applied: it changes something, Tailscale validates it and the guard holds. */
  canApply: (review: Pick<PolicyReview, 'changed' | 'validation' | 'guard'> | null | undefined): boolean =>
    Boolean(review && review.changed && review.validation.ok && review.guard.ok),

  /** Whether a policy live before the last apply can be put back. */
  canRevert: (review: Pick<PolicyReview, 'previous'> | null | undefined): boolean => Boolean(review?.previous),
} as const;
