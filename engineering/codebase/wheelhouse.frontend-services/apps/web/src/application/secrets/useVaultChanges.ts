import {
  onScopeDispose,
  ref,
  shallowRef,
  toValue,
  watch,
  type MaybeRefOrGetter,
} from "vue";
import {
  ApiFailureFactory,
  type ApiFailure,
} from "@wow-two-beta/fe/foundation/http";
import type { Result } from "@wow-two-beta/fe/foundation/results";
import { useQueryCache } from "@/bootstrap/query";
import { secretsApi } from "@/integration/secrets";
import { SecretKeys } from "./SecretKeys";

/** @internal Executes vault writes without retaining plaintext in a mutation cache. */
function useVaultChange() {
  const cache = useQueryCache();
  const loading = ref(false);
  const error = shallowRef<ApiFailure | null>(null);
  let controller: AbortController | null = null;
  let generation = 0;

  /** Aborts transport and prevents a closed editor from receiving a late result. */
  function reset(): void {
    generation += 1;
    controller?.abort();
    controller = null;
    loading.value = false;
    error.value = null;
  }

  /** Runs one scoped write and invalidates metadata after confirmed success. */
  async function run<T>(
    operation: (signal: AbortSignal) => Promise<Result<T, ApiFailure>>,
    invalidates: readonly (readonly unknown[])[],
    signal?: AbortSignal,
  ): Promise<Result<T, ApiFailure>> {
    if (loading.value || signal?.aborted) {
      return { ok: false, failure: ApiFailureFactory.create("cancelled") };
    }
    const revision = generation;
    const request = new AbortController();
    controller = request;
    const abort = () => request.abort();
    signal?.addEventListener("abort", abort, { once: true });
    loading.value = true;
    error.value = null;
    try {
      const result = await operation(request.signal);
      if (revision !== generation || request.signal.aborted) {
        return { ok: false, failure: ApiFailureFactory.create("cancelled") };
      }
      if (result.ok) {
        // Refresh failures must not turn a committed write into a retryable write failure.
        void Promise.allSettled(
          invalidates.map((key) => cache.invalidate(key)),
        );
      } else {
        error.value = result.failure;
      }
      return result;
    } catch {
      const failure = ApiFailureFactory.create(
        request.signal.aborted ? "cancelled" : "transport",
      );
      if (revision === generation) error.value = failure;
      return { ok: false, failure };
    } finally {
      signal?.removeEventListener("abort", abort);
      if (revision === generation) {
        controller = null;
        loading.value = false;
      }
    }
  }

  onScopeDispose(reset);
  return { run, loading, error, reset };
}

/** Creates namespaces in the selected vault. */
export function useNamespaceCreate(vault: MaybeRefOrGetter<string>) {
  const change = useVaultChange();
  watch(() => toValue(vault), change.reset);
  return {
    ...change,
    create: (slug: string, name: string, signal?: AbortSignal) => {
      const current = toValue(vault);
      return change.run(
        (requestSignal) =>
          secretsApi.createNamespace(current, slug, name, requestSignal),
        [SecretKeys.namespaces(current), SecretKeys.hygiene(current)],
        signal,
      );
    },
  };
}

/** Writes secrets and changes serving state without caching plaintext. */
export function useSecretChanges(
  vault: MaybeRefOrGetter<string>,
  ns: MaybeRefOrGetter<string>,
) {
  const change = useVaultChange();
  watch([() => toValue(vault), () => toValue(ns)], change.reset);
  return {
    ...change,
    set: (
      key: string,
      value: string,
      description?: string,
      signal?: AbortSignal,
    ) => {
      const current = toValue(vault);
      const namespace = toValue(ns);
      return change.run(
        (requestSignal) =>
          secretsApi.setSecret(
            current,
            namespace,
            key,
            value,
            description,
            requestSignal,
          ),
        [SecretKeys.secrets(current, namespace), SecretKeys.hygiene(current)],
        signal,
      );
    },
    setDisabled: (key: string, disabled: boolean, signal?: AbortSignal) => {
      const current = toValue(vault);
      const namespace = toValue(ns);
      return change.run(
        (requestSignal) =>
          secretsApi.setSecretState(
            current,
            namespace,
            key,
            disabled,
            requestSignal,
          ),
        [SecretKeys.secrets(current, namespace), SecretKeys.hygiene(current)],
        signal,
      );
    },
  };
}

/** Mints and revokes tokens without caching their one-time plaintext. */
export function useTokenChanges(
  vault: MaybeRefOrGetter<string>,
  ns: MaybeRefOrGetter<string>,
) {
  const change = useVaultChange();
  watch([() => toValue(vault), () => toValue(ns)], change.reset);
  return {
    ...change,
    mint: (name: string, signal?: AbortSignal) => {
      const current = toValue(vault);
      const namespace = toValue(ns);
      return change.run(
        (requestSignal) =>
          secretsApi.mintToken(current, namespace, name, requestSignal),
        [SecretKeys.tokens(current, namespace), SecretKeys.hygiene(current)],
        signal,
      );
    },
    revoke: (id: string, signal?: AbortSignal) => {
      const current = toValue(vault);
      const namespace = toValue(ns);
      return change.run(
        (requestSignal) =>
          secretsApi.revokeToken(current, namespace, id, requestSignal),
        [SecretKeys.tokens(current, namespace), SecretKeys.hygiene(current)],
        signal,
      );
    },
  };
}
