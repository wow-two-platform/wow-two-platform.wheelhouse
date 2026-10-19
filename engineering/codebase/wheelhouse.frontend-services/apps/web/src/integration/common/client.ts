import { createAuthBridge } from "@wow-two-beta/fe/auth";
import {
  createApiClient,
  ApiFailureFactory,
  type ApiFailure,
} from "@wow-two-beta/fe/foundation/http";
import {
  ResultExtensions,
  type Result,
} from "@wow-two-beta/fe/foundation/results";
import type { ZodType } from "zod";
import type { CurrentUser } from "@/domain/auth";

/**
 * Owns the owner's session lifetime: its scope cancels a replaced session's requests and clears its cached records,
 * and the first 401 of a signed-in session expires it once.
 */
export const sessionBridge = createAuthBridge<CurrentUser>({
  getIdentity: (u) => u.login,
});

const client = createApiClient({
  credentials: "same-origin",
  scope: sessionBridge.scope,
  onUnauthorized: sessionBridge.onUnauthorized,
});

/** Options for same-origin management requests; action headers guard owner writes. */
export interface RequestOptions {
  readonly method?: string;
  readonly body?: unknown;
  readonly signal?: AbortSignal | undefined;
  readonly action?: string;
}

/** Decodes the endpoint's consumed shape without exposing response diagnostics as display text. */
function decoder<T>(
  schema: ZodType,
): (value: unknown) => Result<T, ApiFailure> {
  return (value) => {
    const parsed = schema.safeParse(value);
    return parsed.success
      ? ResultExtensions.ok(parsed.data as T)
      : ResultExtensions.fail(ApiFailureFactory.create("protocol"));
  };
}

/** Reads an un-enveloped JSON endpoint through the SDK transport. */
export function request<T>(
  path: string,
  schema: ZodType,
  options: RequestOptions = {},
) {
  return client.request<T>(path, {
    ...options,
    signal: options.signal ?? null,
    unwrap: false,
    decode: decoder<T>(schema),
    ...(options.action
      ? { headers: { "X-Wheelhouse-Action": options.action } }
      : {}),
  });
}

/** Reads a management endpoint's required data envelope and validates its consumed payload. */
export function requestData<T>(
  path: string,
  schema: ZodType,
  options: RequestOptions = {},
) {
  return client.request<T>(path, {
    ...options,
    signal: options.signal ?? null,
    decode: decoder<T>(schema),
    ...(options.action
      ? { headers: { "X-Wheelhouse-Action": options.action } }
      : {}),
  });
}

/**
 * Sends a write whose answer the console does not read: any success counts, empty or with a body, and the
 * caller reloads what changed. Failures keep their status and diagnostics.
 */
export async function requestAccepted(
  path: string,
  options: RequestOptions = {},
): Promise<Result<void, ApiFailure>> {
  const result = await client.request(path, {
    ...options,
    signal: options.signal ?? null,
    response: "text",
    ...(options.action
      ? { headers: { "X-Wheelhouse-Action": options.action } }
      : {}),
  });
  return result.ok ? ResultExtensions.ok(undefined) : result;
}

/** Accepts an explicitly empty successful response for logout and resource deletion. */
export function requestEmpty(path: string, options: RequestOptions = {}) {
  return client.request(path, {
    ...options,
    signal: options.signal ?? null,
    response: "empty",
    ...(options.action
      ? { headers: { "X-Wheelhouse-Action": options.action } }
      : {}),
  });
}

/**
 * The reason an action was refused, when the management API gives one, else the generic failure text.
 * The API passes on only the runner's static reasons and contract key names, never values or command output.
 */
export function failureReason(
  error: { readonly message: string } | null | undefined,
): string {
  const problem = (
    error as { readonly problem?: Readonly<Record<string, unknown>> | null } | null | undefined
  )?.problem;
  const detail = problem?.["detail"];
  if (typeof detail === "string") {
    const text = detail
      .replace(/[\u0000-\u001f\u007f]/g, "")
      .replace(/^Deployment (?:rejected|step failed): /, "")
      .trim();
    if (text.length > 0 && text.length <= 300) return text;
  }
  return error?.message ?? "";
}
