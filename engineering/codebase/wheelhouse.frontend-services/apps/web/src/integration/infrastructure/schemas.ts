import { z } from "zod";
import { InfraJobStatus, JobStepStatus, TailnetNodeRole } from "@/domain/infrastructure";

/** Text the host may leave out or send as null. */
const optionalText = z.string().nullable().default(null);

const JobStepSchema = z.object({
  key: z.string(),
  title: z.string(),
  status: z.enum(JobStepStatus),
  detail: optionalText,
  startedAt: optionalText,
  completedAt: optionalText,
});

const MintedKeySchema = z.object({
  keyId: z.string(),
  tags: z.array(z.string()).default([]),
  expires: z.string(),
  description: z.string().optional(),
  mintedAt: z.string().optional(),
  step: z.string().optional(),
  node: z.string().optional(),
});

const TailnetNodeSchema = z.object({
  id: z.string(),
  name: z.string(),
  hostname: z.string().default(""),
  addresses: z.array(z.string()).default([]),
  tags: z.array(z.string()).default([]),
  online: z.boolean().default(false),
  lastSeen: optionalText,
  keyExpiryDisabled: z.boolean().default(false),
  expires: optionalText,
  created: optionalText,
  os: optionalText,
  authorized: z.boolean().nullable().default(null),
  serverId: optionalText,
  role: z.enum(TailnetNodeRole).catch(TailnetNodeRole.Other),
});

export const TailnetStatusSchema = z.object({
  configured: z.boolean(),
  connected: z.boolean(),
  prodOnly: z.boolean().default(false),
  reason: optionalText,
  tailnet: optionalText,
  clientFile: z.string().default("providers/tailscale/oauth-client.json"),
  nodes: z.array(TailnetNodeSchema).default([]),
  servers: z.array(z.object({ serverId: z.string(), nodeId: optionalText })).default([]),
});

const PolicyDiffHunkSchema = z.object({
  oldStart: z.number().int(),
  newStart: z.number().int(),
  lines: z.array(
    z.object({
      kind: z.enum(["same", "add", "remove"]),
      old: z.number().int().nullable(),
      new: z.number().int().nullable(),
      text: z.string(),
    }),
  ),
});

export const PolicyReviewSchema = z.object({
  etag: z.string().nullable(),
  live: z.string(),
  rendered: z.string(),
  renderedDigest: z.string(),
  changed: z.boolean(),
  diff: z.array(PolicyDiffHunkSchema).default([]),
  hosts: z.array(z.object({ serverId: z.string(), address: z.string() })).default([]),
  validation: z.object({ ok: z.boolean(), problems: z.array(z.string()).default([]) }),
  guard: z.object({ ok: z.boolean(), reason: optionalText }),
  previous: z
    .object({ savedAt: optionalText, actor: optionalText, digest: optionalText })
    .nullable()
    .default(null),
});

export const PolicyAppliedSchema = z.object({
  applied: z.literal(true),
  digest: z.string(),
  etag: optionalText,
  appliedAt: z.string(),
});

export const PolicyRevertedSchema = z.object({
  reverted: z.literal(true),
  digest: z.string(),
  etag: optionalText,
  revertedAt: z.string(),
});

const OneTimeKeySchema = z.object({ publicKey: z.string(), fingerprint: z.string() });

export const EnrollmentKeySchema = OneTimeKeySchema.extend({ serverId: z.string() });

export const EnrollmentSchema = z.object({
  kind: z.literal("enrollment"),
  serverId: z.string(),
  status: z.enum(InfraJobStatus),
  id: z.string().optional(),
  reason: z.string().nullable().optional(),
  actor: z.string().optional(),
  attempt: z.number().int().optional(),
  createdAt: z.string().optional(),
  startedAt: z.string().nullable().optional(),
  completedAt: z.string().nullable().optional(),
  request: z
    .object({
      serverId: z.string(),
      publicAddress: z.string(),
      sshPort: z.number().int(),
      loginUser: z.string(),
      admin: z.string(),
      adminKey: optionalText,
      tailnetHostname: z.string(),
      expectedHostKey: optionalText,
    })
    .optional(),
  facts: z
    .object({
      hostKey: z.string().optional(),
      hostKeyFingerprint: z.string().optional(),
      hostKeyConfirmedBy: z.enum(["owner", "tailnet"]).optional().catch(undefined),
      kitDigest: z.string().optional(),
      tailnetAddress: z.string().optional(),
      nodeId: z.string().optional(),
      nodeName: z.string().optional(),
      publicSshClosed: z.boolean().optional(),
    })
    .default({}),
  mints: z.array(MintedKeySchema).default([]),
  steps: z.array(JobStepSchema).default([]),
  oneTimeKey: OneTimeKeySchema.optional(),
});

const PlacementSiteSchema = z.object({ site: z.string(), node: z.string() });

export const PlacementSchema = z.object({
  id: z.string(),
  kind: z.literal("placement"),
  serverId: z.string(),
  product: z.string(),
  status: z.enum(["queued", "running", "succeeded", "failed", "interrupted"]),
  reason: optionalText,
  actor: z.string().default(""),
  createdAt: z.string(),
  startedAt: optionalText,
  completedAt: optionalText,
  request: z.object({
    serverId: z.string(),
    product: z.string(),
    database: z.boolean(),
    sites: z.array(PlacementSiteSchema).default([]),
  }),
  facts: z
    .object({
      databases: z.array(z.string()).default([]),
      tailnet: z.string().optional(),
      sites: z
        .array(
          z.object({
            site: z.string(),
            environment: z.enum(["dev", "prod"]),
            node: z.string(),
            address: z.string().default(""),
            joined: z.boolean().default(false),
            url: z.string().optional(),
            tailnetAddress: z.string().nullable().optional(),
            online: z.boolean().optional(),
          }),
        )
        .default([]),
    })
    .default({ databases: [], sites: [] }),
  mints: z.array(MintedKeySchema).default([]),
  steps: z.array(JobStepSchema).default([]),
});
