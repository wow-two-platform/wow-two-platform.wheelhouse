/** How an audited action ended. */
export const AuditOutcome = {
  Succeeded: 'succeeded',
  Failed: 'failed',
} as const;
export type AuditOutcome = (typeof AuditOutcome)[keyof typeof AuditOutcome];

/** One action in the append-only, hash-chained audit trail; never carries a secret value. */
export interface AuditEntry {
  /** The entry's position in the chain, starting at 1. */
  sequence: number;
  occurredAt: string;
  actor: string;
  /** The action's stable name, such as `deployment.start`. */
  action: string;
  /** What the action acted on: a target, a product or a vault path. */
  subject: string;
  outcome: AuditOutcome;
  detail?: string | null;
  reason?: string | null;
}

/** Whether every stored entry and link still verifies. An intact chain cannot prove the newest entries were kept. */
export interface AuditVerification {
  intact: boolean;
  entries: number;
  brokenSequence?: number | null;
  reason?: string | null;
}
