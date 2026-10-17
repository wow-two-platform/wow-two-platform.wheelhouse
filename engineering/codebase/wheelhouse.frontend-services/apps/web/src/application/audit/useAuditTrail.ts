import { toValue, type MaybeRefOrGetter } from "vue";

import { useAppQuery } from "@/bootstrap/query";
import { auditApi } from "@/integration/audit";

import { AuditKeys } from "./AuditKeys";

/**
 * One window of the audit trail, newest first; `before` pages back from a sequence number and `product` keeps the
 * entries about one product. The shown window stays while the next one loads.
 */
export function useAuditEntries(
  limit: MaybeRefOrGetter<number>,
  before: MaybeRefOrGetter<number | null>,
  product: MaybeRefOrGetter<string | null> = null,
) {
  return useAppQuery({
    key: () => AuditKeys.list(toValue(limit), toValue(before), toValue(product)),
    queryFn: ({ signal }) => auditApi.list(toValue(limit), toValue(before), toValue(product), signal),
    keepPreviousData: true,
  });
}

/** Whether every stored audit entry and link still verifies. */
export function useAuditVerification() {
  return useAppQuery({
    key: AuditKeys.verification,
    queryFn: ({ signal }) => auditApi.verify(signal),
  });
}
