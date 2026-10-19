import type {
  Enrollment,
  EnrollmentKey,
  Placement,
  PolicyApplied,
  PolicyReverted,
  PolicyReview,
  StartEnrollmentRequest,
  StartPlacementRequest,
  TailnetStatus,
} from "@/domain/infrastructure";
import { requestData } from "@/integration/common";
import {
  EnrollmentKeySchema,
  EnrollmentSchema,
  PlacementSchema,
  PolicyAppliedSchema,
  PolicyReviewSchema,
  PolicyRevertedSchema,
  TailnetStatusSchema,
} from "./schemas";

export {
  EnrollmentKeySchema,
  EnrollmentSchema,
  PlacementSchema,
  PolicyAppliedSchema,
  PolicyReviewSchema,
  PolicyRevertedSchema,
  TailnetStatusSchema,
} from "./schemas";

const server = (slug: string) => `/api/servers/${encodeURIComponent(slug)}/enrollment`;
const product = (slug: string) => `/api/products/${encodeURIComponent(slug)}/placements`;

/** The tailnet Wheelhouse reaches through its OAuth client, and the access policy it renders. */
export const tailnetApi = {
  getStatus: (signal?: AbortSignal) => requestData<TailnetStatus>("/api/tailnet", TailnetStatusSchema, { signal }),
  getPolicy: (signal?: AbortSignal) => requestData<PolicyReview>("/api/tailnet/policy", PolicyReviewSchema, { signal }),
  /** Writes the reviewed policy: the review's ETag and rendered digest, so nothing else is written. */
  applyPolicy: (body: { etag: string; digest: string }, signal?: AbortSignal) =>
    requestData<PolicyApplied>("/api/tailnet/policy/apply", PolicyAppliedSchema, {
      method: "POST", body, signal, action: "tailnet",
    }),
  /** Puts back the policy that was live before the last apply. */
  revertPolicy: (body: { etag: string }, signal?: AbortSignal) =>
    requestData<PolicyReverted>("/api/tailnet/policy/revert", PolicyRevertedSchema, {
      method: "POST", body, signal, action: "tailnet",
    }),
};

/** A server's enrollment: its one-time key, its start or rerun and its live record. */
export const enrollmentApi = {
  getEnrollment: (slug: string, signal?: AbortSignal) =>
    requestData<Enrollment>(server(slug), EnrollmentSchema, { signal }),
  /** Creates the one-time key once; the same key answers again until the enrollment finishes. */
  createKey: (slug: string, signal?: AbortSignal) =>
    requestData<EnrollmentKey>(`${server(slug)}/key`, EnrollmentKeySchema, { method: "POST", signal, action: "server" }),
  /** Starts the enrollment, or reruns one that stopped short from its first unfinished step. */
  start: (slug: string, body: StartEnrollmentRequest, signal?: AbortSignal) =>
    requestData<Enrollment>(server(slug), EnrollmentSchema, { method: "POST", body, signal, action: "server" }),
};

/** A product's placements on servers: databases and site nodes. */
export const placementsApi = {
  /** The product's placements, newest first. */
  listPlacements: (slug: string, signal?: AbortSignal) =>
    requestData<Placement[]>(product(slug), PlacementSchema.array(), { signal }),
  place: (slug: string, body: StartPlacementRequest, signal?: AbortSignal) =>
    requestData<Placement>(product(slug), PlacementSchema, { method: "POST", body, signal, action: "placement" }),
};
