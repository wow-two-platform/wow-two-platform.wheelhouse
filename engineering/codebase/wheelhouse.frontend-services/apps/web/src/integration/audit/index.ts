import type { AuditPage, AuditVerification } from "@/domain/audit";
import { requestData } from "@/integration/common";
import { AuditPageSchema, AuditVerificationSchema } from "./schemas";

/** The append-only audit trail; nothing here changes it. */
export const auditApi = {
  /**
   * One page of the newest `pageSize` entries (1-200), or the older ones after the `nextPageToken` a previous page
   * answered with; with a product, only the entries about it — the product, its services, its targets and its vault
   * namespaces.
   */
  list: (pageSize: number, pageToken?: string | null, product?: string | null, signal?: AbortSignal) =>
    requestData<AuditPage>(
      `/api/audit?pageSize=${pageSize}` +
        (pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : "") +
        (product ? `&product=${encodeURIComponent(product)}` : ""),
      AuditPageSchema,
      { signal },
    ),

  verify: (signal?: AbortSignal) =>
    requestData<AuditVerification>("/api/audit/verification", AuditVerificationSchema, { signal }),
};
