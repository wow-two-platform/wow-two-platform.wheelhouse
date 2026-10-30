import { z } from "zod";
import { AuditOutcome } from "@/domain/audit";

export const AuditEntrySchema = z.object({
  sequence: z.number().int(),
  occurredAt: z.string(),
  actor: z.string(),
  action: z.string(),
  subject: z.string(),
  outcome: z.enum(AuditOutcome),
  detail: z.string().nullable().optional(),
  reason: z.string().nullable().optional(),
});
/** A page of the trail; the last page carries no token. */
export const AuditPageSchema = z.object({
  items: z.array(AuditEntrySchema),
  pageSize: z.number().int(),
  nextPageToken: z
    .string()
    .nullish()
    .transform((value) => value ?? null),
});
export const AuditVerificationSchema = z.object({
  intact: z.boolean(),
  entries: z.number().int(),
  brokenSequence: z.number().int().nullable().optional(),
  reason: z.string().nullable().optional(),
});
