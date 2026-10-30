import { toValue, type MaybeRefOrGetter } from "vue";

import { useAppInfiniteQuery, useAppQuery } from "@/bootstrap/query";
import type { AuditEntry, AuditPage } from "@/domain/audit";
import { auditApi } from "@/integration/audit";

import { AuditKeys } from "./AuditKeys";

/**
 * The audit trail, newest first, one token page at a time: `fetchNextPage` reads the older entries after the last
 * page's `nextPageToken`, so a new entry never shifts a page already read. `product` keeps the entries about one
 * product; another product starts again from its newest entries.
 */
export function useAuditEntries(pageSize: number, product: MaybeRefOrGetter<string | null> = null) {
  return useAppInfiniteQuery<AuditEntry, AuditPage>({
    key: () => AuditKeys.list(pageSize, toValue(product)),
    queryFn: ({ pageParam, signal }) =>
      auditApi.list(pageSize, typeof pageParam === "string" ? pageParam : null, toValue(product), signal),
    initialPageParam: null,
    getNextPageParam: (page) => page.nextPageToken ?? undefined,
    mapPage: (page) => page.items,
  });
}

/** Whether every stored audit entry and link still verifies. */
export function useAuditVerification() {
  return useAppQuery({
    key: AuditKeys.verification,
    queryFn: ({ signal }) => auditApi.verify(signal),
  });
}
