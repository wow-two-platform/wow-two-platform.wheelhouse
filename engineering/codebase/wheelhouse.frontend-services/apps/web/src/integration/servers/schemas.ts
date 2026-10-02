import { z } from "zod";
import { TargetCondition } from "@/domain/deployments";
import { VpsProvider } from "@/domain/servers";

export const ServerSchema = z.object({
  slug: z.string(),
  name: z.string(),
  provider: z.enum(VpsProvider),
  host: z.string(),
  region: z.string(),
  sshUser: z.string(),
  sshPort: z.number().int(),
  ingress: z.object({
    scheme: z.enum(["http", "https"]),
    port: z.number().int().nullable(),
    entryPoints: z.array(z.string()),
    privateEntryPoints: z.array(z.string()),
    certResolver: z.string().nullable(),
    pattern: z.string().nullable(),
    probe: z.string().nullable(),
    privateProbe: z.string().nullable(),
  }),
});
const nullableNumber = z.number().finite().nullable();
const HostSchema = z.object({
  cpus: nullableNumber,
  load: z.tuple([z.number(), z.number(), z.number()]).nullable(),
  memoryTotalBytes: nullableNumber,
  memoryAvailableBytes: nullableNumber,
  uptimeSeconds: nullableNumber,
  disks: z.array(
    z.object({
      path: z.string(),
      totalBytes: z.number(),
      freeBytes: z.number(),
    }),
  ),
});
const ContainerSchema = z.object({
  service: z.string(),
  state: z.string().nullable(),
  health: z.string().nullable(),
  restarts: z.number(),
  startedAt: z.string().nullable(),
  exitCode: nullableNumber,
  cpuPercent: nullableNumber,
  memoryBytes: nullableNumber,
  memoryLimitBytes: nullableNumber,
});
export const ServerVitalsSchema = z.object({
  collectedAt: z.string(),
  targets: z.array(
    z.object({
      targetId: z.string(),
      serverId: z.string(),
      ok: z.boolean(),
      reason: z.string().nullable().optional(),
      project: z.string().optional(),
      host: HostSchema.nullable().optional(),
      containers: ContainerSchema.array().nullable().optional(),
      problems: z.string().array().optional(),
      condition: z.enum(TargetCondition).optional(),
      release: z.string().nullable().optional(),
    }),
  ),
});

export const VitalsSampleSchema = z.object({
  targetId: z.string(),
  serverId: z.string(),
  sampledAt: z.string(),
  readable: z.boolean(),
  loadPercent: nullableNumber,
  memoryPercent: nullableNumber,
  diskPercent: nullableNumber,
  containers: z.number().int(),
  healthyContainers: z.number().int(),
  restarts: z.number().int(),
});
